/* ═══════════════════════════════════════════════════════════════════════
   BACK-FILL: EmployeeDevices.BiometricUserId

   RUN THIS **BEFORE** `dotnet ef database update`.

   WHY THE ORDER MATTERS
   ---------------------
   The new migration adds a UNIQUE index on (DeviceId, BiometricUserId).
   If existing EmployeeDevices rows have an empty BiometricUserId, every one
   of them collides on ('') and the migration fails halfway through, leaving
   the database in a broken state.

   Filling the column first means the index is created against clean data.

   WHAT IT ASSUMES
   ---------------
   That every employee currently uses the same biometric ID on every device.
   That is true today, because the old unique index on
   Employees.BiometricUserId made anything else impossible. After running
   this, correct the rows for anyone whose branch machine assigned them a
   different number (see step 3).
   ═══════════════════════════════════════════════════════════════════════ */

USE ZKAttendanceWebDB;
GO

/* ───────────────────────────────────────────────────────────────────────
   STEP 0 - safety net. Always take a backup before a structural change.
   ─────────────────────────────────────────────────────────────────────── */
-- BACKUP DATABASE ZKAttendanceWebDB
-- TO DISK = 'C:\SqlBackups\ZKAttendanceWebDB_before_multidevice.bak'
-- WITH INIT, NAME = 'Before per-device biometric ID change';
-- GO


/* ───────────────────────────────────────────────────────────────────────
   STEP 1 - add the column manually if the migration has not run yet.
   Skip this if you are letting EF create the column.
   ─────────────────────────────────────────────────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = 'BiometricUserId'
                 AND Object_ID = Object_ID('dbo.EmployeeDevices'))
BEGIN
    ALTER TABLE dbo.EmployeeDevices
        ADD BiometricUserId varchar(12) NOT NULL CONSTRAINT DF_EmpDev_Bio DEFAULT('');

    ALTER TABLE dbo.EmployeeDevices ADD IsEnrolled bit NOT NULL
        CONSTRAINT DF_EmpDev_Enrolled DEFAULT(0);

    ALTER TABLE dbo.EmployeeDevices ADD EnrolledDate datetime2 NULL;

    PRINT 'Columns added to EmployeeDevices.';
END
GO


/* ───────────────────────────────────────────────────────────────────────
   STEP 2a - fill in the ID for links that already exist.
   ─────────────────────────────────────────────────────────────────────── */
UPDATE ed
SET    ed.BiometricUserId = e.BiometricUserId,
       ed.IsEnrolled      = 1,
       ed.EnrolledDate    = ISNULL(ed.CreatedDate, GETDATE())
FROM   dbo.EmployeeDevices ed
JOIN   dbo.Employees       e ON e.EmployeeId = ed.EmployeeId
WHERE  ed.BiometricUserId = '' OR ed.BiometricUserId IS NULL;

PRINT CONCAT('Existing links back-filled: ', @@ROWCOUNT);
GO


/* ───────────────────────────────────────────────────────────────────────
   STEP 2b - create links for employees who have none.

   An employee with no EmployeeDevices row would simply never resolve during
   sync: their punches would land with EmployeeId = NULL and they would look
   permanently absent. Linking them to every active device reproduces the old
   behaviour, where one biometric ID worked everywhere.
   ─────────────────────────────────────────────────────────────────────── */
INSERT INTO dbo.EmployeeDevices
       (EmployeeId, DeviceId, BiometricUserId, IsEnrolled, IsActive, CreatedDate)
SELECT  e.EmployeeId,
        d.DeviceId,
        e.BiometricUserId,
        1, 1, GETDATE()
FROM    dbo.Employees e
CROSS   JOIN dbo.Devices d
WHERE   e.IsActive = 1
  AND   d.IsActive = 1
  AND   e.BiometricUserId <> ''
  AND   NOT EXISTS (SELECT 1 FROM dbo.EmployeeDevices ed
                    WHERE ed.EmployeeId = e.EmployeeId
                      AND ed.DeviceId   = d.DeviceId);

PRINT CONCAT('New links created: ', @@ROWCOUNT);
GO


/* ───────────────────────────────────────────────────────────────────────
   STEP 3 - CHECK FOR COLLISIONS BEFORE CREATING THE UNIQUE INDEX.

   This is the query that decides whether the migration will succeed.
   It must return ZERO rows. Anything it returns means two employees share
   one biometric ID on the same device, which the index will reject.

   Fix each one by editing the employee whose number is wrong, then re-run.
   ─────────────────────────────────────────────────────────────────────── */
SELECT  d.DeviceName,
        ed.BiometricUserId,
        COUNT(*)                       AS ClashingEmployees,
        STRING_AGG(e.EmployeeName, ' | ') AS Names
FROM    dbo.EmployeeDevices ed
JOIN    dbo.Devices   d ON d.DeviceId   = ed.DeviceId
JOIN    dbo.Employees e ON e.EmployeeId = ed.EmployeeId
GROUP BY d.DeviceName, ed.BiometricUserId
HAVING  COUNT(*) > 1;
GO

/* Also check nothing was left empty. Must return zero. */
SELECT COUNT(*) AS RowsStillEmpty
FROM   dbo.EmployeeDevices
WHERE  BiometricUserId = '' OR BiometricUserId IS NULL;
GO


/* ───────────────────────────────────────────────────────────────────────
   STEP 4 - now run the migration:

       dotnet ef migrations add AddPerDeviceBiometricId
       dotnet ef database update

   ─────────────────────────────────────────────────────────────────────── */


/* ───────────────────────────────────────────────────────────────────────
   STEP 5 - link historic punches to employees.

   AttendanceLogs rows recorded before this change may have EmployeeId NULL.
   This resolves them through the new per-device mapping so old attendance
   still appears on the screen (which now groups by EmployeeId).
   ─────────────────────────────────────────────────────────────────────── */
UPDATE al
SET    al.EmployeeId = ed.EmployeeId
FROM   dbo.AttendanceLogs al
JOIN   dbo.EmployeeDevices ed
       ON  ed.DeviceId        = al.DeviceId
       AND ed.BiometricUserId = al.BiometricUserId
WHERE  al.EmployeeId IS NULL;

PRINT CONCAT('Historic punches linked to employees: ', @@ROWCOUNT);
GO

/* Whatever is still NULL has no mapping on that device.
   These show up in the portal under unregistered biometric IDs. */
SELECT  d.DeviceName,
        al.BiometricUserId,
        COUNT(*)          AS PunchCount,
        MIN(al.AttendanceTime) AS FirstSeen,
        MAX(al.AttendanceTime) AS LastSeen
FROM    dbo.AttendanceLogs al
JOIN    dbo.Devices d ON d.DeviceId = al.DeviceId
WHERE   al.EmployeeId IS NULL
GROUP BY d.DeviceName, al.BiometricUserId
ORDER BY PunchCount DESC;
GO
