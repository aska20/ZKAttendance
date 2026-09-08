/* ============================================================================
   AttendanceApprovals - manual creation script

   Prefer the EF route:
       Add-Migration AddAttendanceApprovals
       Update-Database

   EF generates the migration from the DbContext, which already contains the
   entity, and writes the paired .Designer.cs that carries the [Migration]
   attribute. A migration without that attribute is invisible to EF, which is
   why this table has to come from EF or from this script, not from a
   hand-written migration class.

   Use this script only if you would rather not run migrations. Safe to re-run.
   ============================================================================ */

IF OBJECT_ID('dbo.AttendanceApprovals', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceApprovals (
        ApprovalId      INT IDENTITY(1,1) NOT NULL,
        EmployeeId      INT               NOT NULL,
        AttendanceDate  DATE              NOT NULL,
        Status          INT               NOT NULL CONSTRAINT DF_Approval_Status DEFAULT (1),
        FirstCheckIn    DATETIME2         NULL,
        MinutesLate     INT               NOT NULL CONSTRAINT DF_Approval_Late DEFAULT (0),
        DecidedBy       NVARCHAR(100)     NULL,
        DecidedDate     DATETIME2         NULL,
        Note            NVARCHAR(500)     NULL,
        CreatedDate     DATETIME2         NOT NULL CONSTRAINT DF_Approval_Created DEFAULT (GETDATE()),
        CONSTRAINT PK_AttendanceApprovals PRIMARY KEY (ApprovalId),
        CONSTRAINT FK_AttendanceApprovals_Employees_EmployeeId
            FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees (EmployeeId) ON DELETE CASCADE
    );

    -- One decision per employee per day.
    CREATE UNIQUE INDEX IX_AttendanceApproval_Employee_Date
        ON dbo.AttendanceApprovals (EmployeeId, AttendanceDate);

    -- The queue is read by status and date every time the page opens.
    CREATE INDEX IX_AttendanceApproval_Status_Date
        ON dbo.AttendanceApprovals (Status, AttendanceDate);

    PRINT 'AttendanceApprovals created.';
END
ELSE
    PRINT 'AttendanceApprovals already exists.';
GO

/* The office-hours defaults are optional. AttendancePolicyService falls back to
   10:00 / 15 min / 11:00 when these rows are absent, and writes them the first
   time Settings is saved. Inserting them just makes the values visible in the
   table straight away. */
MERGE INTO dbo.SystemSettings AS target
USING (VALUES
    ('OfficeStartTime',              '10:00', 'Working day start time'),
    ('OfficeEndTime',                '18:00', 'Working day end time'),
    ('GraceMinutes',                 '15',    'Minutes after start that still count as on time'),
    ('ApprovalRequiredAfterMinutes', '60',    'Minutes after start beyond which an admin must approve'),
    ('RequireApprovalForLate',       'false', 'Also require approval between grace and the cut-off'),
    ('CloseGraceMinutes',            '30',    'Minutes after end time before a no-show is marked absent'),
    ('HalfDayUnderHours',            '4',     'Hours below which a present day counts as a half day')
) AS source (SettingKey, SettingValue, Description)
ON target.SettingKey = source.SettingKey AND target.Category = 'Attendance'
WHEN NOT MATCHED THEN
    INSERT (SettingKey, SettingValue, Category, Description, IsActive, CreatedDate)
    VALUES (source.SettingKey, source.SettingValue, 'Attendance', source.Description, 1, GETDATE());
GO
