/* ============================================================================
   ZKAttendance — clean slate
   ----------------------------------------------------------------------------
   Section 1 wipes everything GENERATED at runtime (punches, sync history,
   device error/status logs) and keeps what you SET UP.

   Section 2 (optional, commented) also removes leftover test devices/branches.

   Run in SSMS against ZKAttendanceWebDB.
   BEFORE RUNNING: stop the API (Ctrl+C in the `dotnet run` window).
   With SyncConfiguration:EnableAutoSync = false (the new default) nothing
   regenerates after you restart.
   ============================================================================ */

USE ZKAttendanceWebDB;
GO

-- ── Section 1: runtime data ────────────────────────────────────────────────
BEGIN TRAN;
    DELETE FROM AttendanceLogs;
    DELETE FROM SyncLogs;
    DELETE FROM DeviceErrors;
    DELETE FROM DeviceStatuses;
COMMIT;
GO
PRINT 'Runtime data cleared.';
GO

/* ── Section 2: leftover test devices / branches (OPTIONAL) ─────────────────
   Uncomment to run. Adjust the names to match what you created.

BEGIN TRAN;
    DELETE ed FROM EmployeeDevices ed
      JOIN Devices d ON d.DeviceId = ed.DeviceId
      WHERE d.DeviceName IN ('OV dev', 'ZK Test');

    DELETE FROM Devices  WHERE DeviceName IN ('OV dev', 'ZK Test');
    DELETE FROM Branches WHERE BranchName IN ('OV test') OR (BranchName = '' OR BranchName IS NULL);

    -- keep "door" (your real device) but make sure it is active and reachable:
    UPDATE Devices SET IsActive = 1 WHERE DeviceName = 'door';
COMMIT;
GO
*/

/* ── Section 3: full employee reset (OPTIONAL, DESTRUCTIVE) ─────────────────
   Removes ALL employees and their mappings. Only if you want to start people
   from zero too.

-- DELETE FROM EmployeeDevices;
-- DELETE FROM EmployeeBranches;
-- DELETE FROM EmployeeShiftAssignments;
-- UPDATE ApiUsers SET EmployeeId = NULL;
-- DELETE FROM Employees;
*/
