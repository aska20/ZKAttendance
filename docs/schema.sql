/* ============================================================================
   ZKAttendance - Database Schema (Microsoft SQL Server)

   Biometric attendance management with ZKTeco terminals, dual AD/BS calendar
   support, multi-branch/multi-device operation and late-arrival approval.

   20 tables in 6 groups:

     Organisation   Branches, Departments, Employees, EmployeeBranches
     Devices        Devices, EmployeeDevices, DeviceStatuses, DeviceErrors, SyncLogs
     Enrolment      FingerprintTemplates, PendingEnrollments
     Attendance     AttendanceLogs, AttendanceApprovals, Holidays,
                    WorkShifts, EmployeeShiftAssignments
     Security       ApiUsers, RefreshTokens
     System         SystemSettings, Notifications

   Generated from the EF Core model, so it matches what Update-Database
   produces. This file is for documentation and for standing up a database
   by hand; migrations remain the source of truth.
   ============================================================================ */

/* ---------------------------------------------------------------------------
   1. ORGANISATION
   --------------------------------------------------------------------------- */

-- A physical office. Devices, employees and attendance all hang off a branch.
CREATE TABLE Branches (
    BranchId        INT IDENTITY(1,1)   NOT NULL,
    BranchCode      NVARCHAR(20)        NOT NULL,
    BranchName      NVARCHAR(100)       NOT NULL,
    City            NVARCHAR(50)        NULL,
    Address         NVARCHAR(200)       NULL,
    ContactPerson   NVARCHAR(100)       NULL,
    ContactPhone    NVARCHAR(20)        NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT (1),
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_Branches_Created DEFAULT (GETDATE()),
    ModifiedDate    DATETIME2           NULL,
    LastSyncTime    DATETIME2           NULL,
    CONSTRAINT PK_Branches PRIMARY KEY (BranchId)
);
CREATE UNIQUE INDEX IX_Branch_Code ON Branches (BranchCode);


-- Self-referencing hierarchy: a department may sit under a parent department.
CREATE TABLE Departments (
    DepartmentId        INT IDENTITY(1,1)   NOT NULL,
    DepartmentName      NVARCHAR(100)       NOT NULL,
    DepartmentCode      NVARCHAR(50)        NULL,
    ParentDepartmentId  INT                 NULL,
    Description         NVARCHAR(500)       NULL,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1),
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_Departments_Created DEFAULT (GETDATE()),
    ModifiedDate        DATETIME2           NULL,
    CONSTRAINT PK_Departments PRIMARY KEY (DepartmentId),
    -- NO ACTION, not CASCADE: a self-referencing cascade is rejected by SQL Server.
    CONSTRAINT FK_Departments_Parent FOREIGN KEY (ParentDepartmentId)
        REFERENCES Departments (DepartmentId) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX IX_Department_Name ON Departments (DepartmentName);


-- Shift definition. StartTime/EndTime drive lateness and the absent cut-off.
CREATE TABLE WorkShifts (
    ShiftId             INT IDENTITY(1,1)   NOT NULL,
    ShiftName           NVARCHAR(50)        NOT NULL,
    StartTime           TIME                NOT NULL,
    EndTime             TIME                NOT NULL,
    LateMinutes         INT                 NOT NULL CONSTRAINT DF_WorkShifts_Late DEFAULT (15),
    EarlyMinutes        INT                 NOT NULL CONSTRAINT DF_WorkShifts_Early DEFAULT (15),
    CheckInWindowStart  TIME                NULL,
    CheckInWindowEnd    TIME                NULL,
    CheckOutWindowStart TIME                NULL,
    CheckOutWindowEnd   TIME                NULL,
    RequireCheckIn      BIT                 NOT NULL CONSTRAINT DF_WorkShifts_ReqIn DEFAULT (1),
    RequireCheckOut     BIT                 NOT NULL CONSTRAINT DF_WorkShifts_ReqOut DEFAULT (1),
    WorkMinutes         INT                 NOT NULL CONSTRAINT DF_WorkShifts_Work DEFAULT (480),
    BreakMinutes        INT                 NOT NULL CONSTRAINT DF_WorkShifts_Break DEFAULT (0),
    Color               INT                 NOT NULL CONSTRAINT DF_WorkShifts_Color DEFAULT (16715535),
    Description         NVARCHAR(200)       NULL,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_WorkShifts_IsActive DEFAULT (1),
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_WorkShifts_Created DEFAULT (GETDATE()),
    ModifiedDate        DATETIME2           NULL,
    CONSTRAINT PK_WorkShifts PRIMARY KEY (ShiftId),
    CONSTRAINT CK_WorkShifts_Times CHECK (EndTime <> StartTime)
);
CREATE INDEX IX_WorkShift_Name ON WorkShifts (ShiftName);


-- A person. BiometricUserId is the enrol number the terminal reports in a punch,
-- which is why it is varchar(12): it is device data, not free text.
CREATE TABLE Employees (
    EmployeeId          INT IDENTITY(1,1)   NOT NULL,
    BiometricUserId     VARCHAR(12)         NOT NULL,
    EmployeeName        NVARCHAR(100)       NOT NULL,
    SSN                 NVARCHAR(20)        NULL,
    Gender              NVARCHAR(2)         NULL,
    Title               NVARCHAR(50)        NULL,
    PhoneNumber         NVARCHAR(20)        NULL,
    Email               NVARCHAR(150)       NULL,
    BirthDate           DATETIME2           NULL,
    HireDate            DATETIME2           NULL,
    PhotoUrl            NVARCHAR(MAX)       NULL,
    DepartmentId        INT                 NULL,
    DefaultShiftId      INT                 NULL,
    CheckAttendance     BIT                 NOT NULL CONSTRAINT DF_Employees_ChkAtt DEFAULT (1),
    CheckLate           BIT                 NOT NULL CONSTRAINT DF_Employees_ChkLate DEFAULT (1),
    CheckEarly          BIT                 NOT NULL CONSTRAINT DF_Employees_ChkEarly DEFAULT (1),
    CheckOvertime       BIT                 NOT NULL CONSTRAINT DF_Employees_ChkOt DEFAULT (1),
    CheckHoliday        BIT                 NOT NULL CONSTRAINT DF_Employees_ChkHol DEFAULT (1),
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
    ApprovalStatus      NVARCHAR(20)        NOT NULL CONSTRAINT DF_Employees_Approval DEFAULT ('Approved'),
    RequestedByUserId   INT                 NULL,
    ApprovedByUserId    INT                 NULL,
    ApprovedDate        DATETIME2           NULL,
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_Employees_Created DEFAULT (GETDATE()),
    ModifiedDate        DATETIME2           NULL,
    CONSTRAINT PK_Employees PRIMARY KEY (EmployeeId),
    CONSTRAINT FK_Employees_Department FOREIGN KEY (DepartmentId)
        REFERENCES Departments (DepartmentId) ON DELETE SET NULL,
    CONSTRAINT FK_Employees_Shift FOREIGN KEY (DefaultShiftId)
        REFERENCES WorkShifts (ShiftId) ON DELETE SET NULL,
    CONSTRAINT CK_Employees_Approval
        CHECK (ApprovalStatus IN ('Approved', 'Pending', 'Rejected')),
    -- A hire date in the future makes every prior day read as "not joined".
    CONSTRAINT CK_Employees_HireDate CHECK (HireDate IS NULL OR HireDate <= GETDATE())
);
CREATE INDEX IX_Employee_BiometricUserId ON Employees (BiometricUserId);
CREATE INDEX IX_Employee_Department ON Employees (DepartmentId);


-- Which branches a person may attend at. Many-to-many.
CREATE TABLE EmployeeBranches (
    EmployeeBranchId    INT IDENTITY(1,1)   NOT NULL,
    EmployeeId          INT                 NOT NULL,
    BranchId            INT                 NOT NULL,
    AssignedDate        DATETIME2           NOT NULL CONSTRAINT DF_EmpBranch_Assigned DEFAULT (GETDATE()),
    IsActive            BIT                 NOT NULL CONSTRAINT DF_EmpBranch_IsActive DEFAULT (1),
    DeactivatedDate     DATETIME2           NULL,
    Notes               NVARCHAR(500)       NULL,
    CONSTRAINT PK_EmployeeBranches PRIMARY KEY (EmployeeBranchId),
    CONSTRAINT FK_EmployeeBranches_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE CASCADE,
    CONSTRAINT FK_EmployeeBranches_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX IX_EmployeeBranch_Unique ON EmployeeBranches (EmployeeId, BranchId);


-- Shift history. EffectiveFrom/To let a person change shift without losing the past.
CREATE TABLE EmployeeShiftAssignments (
    AssignmentId    INT IDENTITY(1,1)   NOT NULL,
    EmployeeId      INT                 NOT NULL,
    ShiftId         INT                 NOT NULL,
    EffectiveFrom   DATE                NOT NULL,
    EffectiveTo     DATE                NULL,
    Notes           NVARCHAR(500)       NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_EmpShift_IsActive DEFAULT (1),
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_EmpShift_Created DEFAULT (GETDATE()),
    ModifiedDate    DATETIME2           NULL,
    CONSTRAINT PK_EmployeeShiftAssignments PRIMARY KEY (AssignmentId),
    CONSTRAINT FK_EmpShift_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE CASCADE,
    CONSTRAINT FK_EmpShift_Shift FOREIGN KEY (ShiftId)
        REFERENCES WorkShifts (ShiftId) ON DELETE NO ACTION,
    CONSTRAINT CK_EmpShift_Range CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE INDEX IX_EmployeeShift_Effective ON EmployeeShiftAssignments (EmployeeId, EffectiveFrom);


/* ---------------------------------------------------------------------------
   2. DEVICES
   --------------------------------------------------------------------------- */

-- A ZKTeco terminal. Role: 0 = Slave, 1 = Master.
-- Fingerprints are captured on the Master and copied to the Slaves.
CREATE TABLE Devices (
    DeviceId                INT IDENTITY(1,1)   NOT NULL,
    BranchId                INT                 NOT NULL,
    DeviceName              NVARCHAR(100)       NOT NULL,
    DeviceIP                NVARCHAR(45)        NOT NULL,
    DevicePort              INT                 NOT NULL CONSTRAINT DF_Devices_Port DEFAULT (4370),
    SerialNumber            NVARCHAR(50)        NULL,
    DeviceModel             NVARCHAR(50)        NULL,
    CommPassword            INT                 NOT NULL CONSTRAINT DF_Devices_Comm DEFAULT (0),
    Role                    INT                 NOT NULL CONSTRAINT DF_Devices_Role DEFAULT (0),
    IsProvisioned           BIT                 NOT NULL CONSTRAINT DF_Devices_Prov DEFAULT (0),
    LastEnrollmentPullDate  DATETIME2           NULL,
    IsActive                BIT                 NOT NULL CONSTRAINT DF_Devices_IsActive DEFAULT (1),
    IsOnline                BIT                 NOT NULL CONSTRAINT DF_Devices_IsOnline DEFAULT (0),
    LastCheckTime           DATETIME2           NULL,
    LastConnectionTime      DATETIME2           NULL,
    ConnectionStatus        NVARCHAR(50)        NULL,
    CreatedDate             DATETIME2           NOT NULL CONSTRAINT DF_Devices_Created DEFAULT (GETDATE()),
    ModifiedDate            DATETIME2           NULL,
    CONSTRAINT PK_Devices PRIMARY KEY (DeviceId),
    CONSTRAINT FK_Devices_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE CASCADE,
    CONSTRAINT CK_Devices_Role CHECK (Role IN (0, 1)),
    CONSTRAINT CK_Devices_Port CHECK (DevicePort BETWEEN 1 AND 65535)
);
-- One terminal cannot be reached at two addresses, and two terminals cannot
-- share one address.
CREATE UNIQUE INDEX IX_Device_IP_Port ON Devices (DeviceIP, DevicePort);

-- Filtered unique index: at most ONE active Master across the whole system.
-- Enforced in the database because two masters would mean two competing
-- sources of truth for fingerprint templates.
CREATE UNIQUE INDEX UX_Device_SingleMaster ON Devices (Role)
    WHERE Role = 1 AND IsActive = 1;


-- Which enrol number a given terminal knows a person by. Devices allocate their
-- own numbers, so 1017 at head office can be 88 at a branch. This table is the
-- only place that mapping lives.
CREATE TABLE EmployeeDevices (
    EmployeeDeviceId    INT IDENTITY(1,1)   NOT NULL,
    EmployeeId          INT                 NOT NULL,
    DeviceId            INT                 NOT NULL,
    BiometricUserId     VARCHAR(12)         NOT NULL,
    IsEnrolled          BIT                 NOT NULL CONSTRAINT DF_EmpDevice_Enrolled DEFAULT (0),
    EnrolledDate        DATETIME2           NULL,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_EmpDevice_IsActive DEFAULT (1),
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_EmpDevice_Created DEFAULT (GETDATE()),
    ModifiedDate        DATETIME2           NULL,
    CONSTRAINT PK_EmployeeDevices PRIMARY KEY (EmployeeDeviceId),
    CONSTRAINT FK_EmployeeDevices_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE CASCADE,
    CONSTRAINT FK_EmployeeDevices_Device FOREIGN KEY (DeviceId)
        REFERENCES Devices (DeviceId) ON DELETE NO ACTION
);
-- A person appears once per device...
CREATE UNIQUE INDEX IX_EmployeeDevice_Employee_Device
    ON EmployeeDevices (EmployeeId, DeviceId);
-- ...and one enrol number on a device belongs to exactly one person. Without
-- this, two people could share a number and their punches would be
-- indistinguishable.
CREATE UNIQUE INDEX IX_EmployeeDevice_Device_BiometricId
    ON EmployeeDevices (DeviceId, BiometricUserId);


-- Health snapshot taken by the monitor. Append-only history.
CREATE TABLE DeviceStatuses (
    StatusId            BIGINT IDENTITY(1,1) NOT NULL,
    DeviceId            INT                 NOT NULL,
    BranchId            INT                 NOT NULL,
    IsOnline            BIT                 NOT NULL CONSTRAINT DF_DevStatus_Online DEFAULT (0),
    StatusTime          DATETIME2           NOT NULL CONSTRAINT DF_DevStatus_Time DEFAULT (GETDATE()),
    DeviceTime          DATETIME2           NULL,
    LastConnectionTime  DATETIME2           NULL,
    LastUpdateTime      DATETIME2           NOT NULL CONSTRAINT DF_DevStatus_Upd DEFAULT (GETDATE()),
    StatusMessage       NVARCHAR(500)       NULL,
    UserCount           INT                 NULL,
    LogCount            INT                 NULL,
    FaceCount           INT                 NULL,
    FirmwareVersion     NVARCHAR(50)        NULL,
    FreeSpace           BIGINT              NULL,
    TotalSpace          BIGINT              NULL,
    SerialNumber        NVARCHAR(50)        NULL,
    DeviceModel         NVARCHAR(50)        NULL,
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_DevStatus_Created DEFAULT (GETDATE()),
    CONSTRAINT PK_DeviceStatuses PRIMARY KEY (StatusId),
    CONSTRAINT FK_DeviceStatuses_Device FOREIGN KEY (DeviceId)
        REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    CONSTRAINT FK_DeviceStatuses_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE NO ACTION
);
CREATE INDEX IX_DeviceStatus_Device_Time ON DeviceStatuses (DeviceId, StatusTime);
CREATE INDEX IX_DeviceStatus_IsOnline ON DeviceStatuses (IsOnline);


CREATE TABLE DeviceErrors (
    ErrorId             BIGINT IDENTITY(1,1) NOT NULL,
    DeviceId            INT                 NOT NULL,
    BranchId            INT                 NOT NULL,
    ErrorMessage        NVARCHAR(500)       NOT NULL,
    ErrorCode           NVARCHAR(50)        NULL,
    ErrorDateTime       DATETIME2           NOT NULL CONSTRAINT DF_DevError_Time DEFAULT (GETDATE()),
    Severity            NVARCHAR(20)        NOT NULL CONSTRAINT DF_DevError_Sev DEFAULT ('Medium'),
    IsResolved          BIT                 NOT NULL CONSTRAINT DF_DevError_Resolved DEFAULT (0),
    ResolvedDateTime    DATETIME2           NULL,
    ResolvedBy          NVARCHAR(100)       NULL,
    Resolution          NVARCHAR(1000)      NULL,
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_DevError_Created DEFAULT (GETDATE()),
    CONSTRAINT PK_DeviceErrors PRIMARY KEY (ErrorId),
    CONSTRAINT FK_DeviceErrors_Device FOREIGN KEY (DeviceId)
        REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    CONSTRAINT FK_DeviceErrors_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE NO ACTION,
    CONSTRAINT CK_DeviceErrors_Severity
        CHECK (Severity IN ('Low', 'Medium', 'High', 'Critical'))
);
CREATE INDEX IX_DeviceError_Device_Time ON DeviceErrors (DeviceId, ErrorDateTime);
CREATE INDEX IX_DeviceError_IsResolved ON DeviceErrors (IsResolved);


-- One row per sync run. RecordCount vs NewRecordCount vs DuplicateCount is how
-- a partial or repeated pull is diagnosed after the fact.
CREATE TABLE SyncLogs (
    SyncId          INT IDENTITY(1,1)   NOT NULL,
    DeviceId        INT                 NOT NULL,
    BranchId        INT                 NOT NULL,
    StartTime       DATETIME2           NOT NULL CONSTRAINT DF_SyncLogs_Start DEFAULT (GETDATE()),
    EndTime         DATETIME2           NULL,
    Status          NVARCHAR(20)        NOT NULL CONSTRAINT DF_SyncLogs_Status DEFAULT ('Pending'),
    RecordCount     INT                 NOT NULL CONSTRAINT DF_SyncLogs_Rec DEFAULT (0),
    NewRecordCount  INT                 NOT NULL CONSTRAINT DF_SyncLogs_New DEFAULT (0),
    DuplicateCount  INT                 NOT NULL CONSTRAINT DF_SyncLogs_Dup DEFAULT (0),
    ErrorMessage    NVARCHAR(1000)      NULL,
    ErrorCode       NVARCHAR(50)        NULL,
    ServerName      NVARCHAR(100)       NULL,
    RetryAttempt    INT                 NOT NULL CONSTRAINT DF_SyncLogs_Retry DEFAULT (0),
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_SyncLogs_Created DEFAULT (GETDATE()),
    CONSTRAINT PK_SyncLogs PRIMARY KEY (SyncId),
    CONSTRAINT FK_SyncLogs_Device FOREIGN KEY (DeviceId)
        REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    CONSTRAINT FK_SyncLogs_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE NO ACTION,
    CONSTRAINT CK_SyncLogs_Status
        CHECK (Status IN ('Pending', 'Running', 'Success', 'Failed', 'Partial'))
);
CREATE INDEX IX_SyncLog_Device_Time ON SyncLogs (DeviceId, StartTime);
CREATE INDEX IX_SyncLog_Status ON SyncLogs (Status);


/* ---------------------------------------------------------------------------
   3. ENROLMENT
   --------------------------------------------------------------------------- */

-- Cached fingerprint templates, pulled from the Master.
-- This cache is what makes adding a third or fourth terminal a background job
-- instead of calling every employee back to the sensor. TemplateFormatVersion
-- matters: a v10 template written to a v9 terminal is accepted and then never
-- matches, so the version travels with the bytes.
CREATE TABLE FingerprintTemplates (
    TemplateId              INT IDENTITY(1,1)   NOT NULL,
    EmployeeId              INT                 NOT NULL,
    FingerIndex             INT                 NOT NULL,
    TemplateData            VARBINARY(MAX)      NOT NULL,
    TemplateFormatVersion   INT                 NOT NULL CONSTRAINT DF_Template_Ver DEFAULT (10),
    SourceDeviceId          INT                 NOT NULL,
    CapturedDate            DATETIME2           NOT NULL CONSTRAINT DF_Template_Captured DEFAULT (GETDATE()),
    CONSTRAINT PK_FingerprintTemplates PRIMARY KEY (TemplateId),
    CONSTRAINT FK_Templates_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE CASCADE,
    CONSTRAINT CK_Template_Finger CHECK (FingerIndex BETWEEN 0 AND 9),
    CONSTRAINT CK_Template_Version CHECK (TemplateFormatVersion IN (9, 10))
);
CREATE UNIQUE INDEX UX_Template_Employee_Finger
    ON FingerprintTemplates (EmployeeId, FingerIndex);


-- Someone enrolled directly on a terminal who has no employee record here yet.
-- The system discovers them on sync and holds them for review rather than
-- silently creating a person or silently dropping their punches.
CREATE TABLE PendingEnrollments (
    PendingEnrollmentId INT IDENTITY(1,1)   NOT NULL,
    DeviceId            INT                 NOT NULL,
    DeviceUserId        VARCHAR(12)         NOT NULL,
    NameOnDevice        NVARCHAR(100)       NULL,
    FingerCount         INT                 NOT NULL CONSTRAINT DF_Pending_Fingers DEFAULT (0),
    Privilege           INT                 NOT NULL CONSTRAINT DF_Pending_Priv DEFAULT (0),
    Status              INT                 NOT NULL CONSTRAINT DF_Pending_Status DEFAULT (0),
    DiscoveredDate      DATETIME2           NOT NULL CONSTRAINT DF_Pending_Found DEFAULT (GETDATE()),
    ApprovedEmployeeId  INT                 NULL,
    ReviewedDate        DATETIME2           NULL,
    ReviewedBy          NVARCHAR(100)       NULL,
    RejectionReason     NVARCHAR(400)       NULL,
    CONSTRAINT PK_PendingEnrollments PRIMARY KEY (PendingEnrollmentId),
    CONSTRAINT FK_Pending_Device FOREIGN KEY (DeviceId)
        REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    CONSTRAINT FK_Pending_Employee FOREIGN KEY (ApprovedEmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE SET NULL
);
CREATE UNIQUE INDEX UX_Pending_Device_UserId
    ON PendingEnrollments (DeviceId, DeviceUserId);


/* ---------------------------------------------------------------------------
   4. ATTENDANCE
   --------------------------------------------------------------------------- */

-- Raw punches. One row per scan, so a person who steps out for lunch produces
-- four rows in a day.
--
-- EmployeeId is NULLABLE on purpose: a punch can arrive from a terminal for an
-- enrol number nobody has claimed yet. Discarding it would lose attendance;
-- BiometricUserId is kept so it can be attributed later.
CREATE TABLE AttendanceLogs (
    LogId           BIGINT IDENTITY(1,1) NOT NULL,
    BiometricUserId VARCHAR(12)         NOT NULL,
    EmployeeId      INT                 NULL,
    DeviceId        INT                 NOT NULL,
    BranchId        INT                 NOT NULL,
    AttendanceTime  DATETIME2           NOT NULL,
    AttendanceType  NVARCHAR(50)        NULL,   -- Check In / Check Out / Break Out / Break In
    VerifyMethod    NVARCHAR(50)        NULL,   -- Fingerprint / Face / Card / Password
    WorkCode        INT                 NULL,
    IsSynced        BIT                 NOT NULL CONSTRAINT DF_AttLog_Synced DEFAULT (0),
    SyncedDate      DATETIME2           NULL,
    IsProcessed     BIT                 NOT NULL CONSTRAINT DF_AttLog_Processed DEFAULT (0),
    ProcessedDate   DATETIME2           NULL,
    IsManual        BIT                 NOT NULL CONSTRAINT DF_AttLog_Manual DEFAULT (0),
    Notes           NVARCHAR(200)       NULL,
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_AttLog_Created DEFAULT (GETDATE()),
    CONSTRAINT PK_AttendanceLogs PRIMARY KEY (LogId),
    CONSTRAINT FK_AttendanceLogs_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE SET NULL,
    CONSTRAINT FK_AttendanceLogs_Device FOREIGN KEY (DeviceId)
        REFERENCES Devices (DeviceId) ON DELETE NO ACTION,
    CONSTRAINT FK_AttendanceLogs_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE NO ACTION
);
-- The idempotency guard for sync. Most ZKTeco models cannot filter by date and
-- hand back their whole log every time, so the same punch is re-offered on
-- every pull. This index is what makes a repeated sync harmless.
CREATE UNIQUE INDEX IX_AttendanceLog_Unique
    ON AttendanceLogs (BiometricUserId, AttendanceTime, DeviceId);
CREATE INDEX IX_AttendanceLog_IsSynced ON AttendanceLogs (IsSynced);
CREATE INDEX IX_AttendanceLog_IsProcessed ON AttendanceLogs (IsProcessed);
CREATE INDEX IX_AttendanceLog_Employee_Time ON AttendanceLogs (EmployeeId, AttendanceTime);


-- One approval decision per employee per day.
--
-- Separate from AttendanceLogs because a person scans many times a day but
-- there is only ever one decision about whether that day counts. Hanging it off
-- a punch would mean re-choosing the owning punch every time an earlier scan
-- arrives late from an offline device.
--
-- Status: 0 AutoApproved, 1 Pending, 2 Approved, 3 Rejected
CREATE TABLE AttendanceApprovals (
    ApprovalId      INT IDENTITY(1,1)   NOT NULL,
    EmployeeId      INT                 NOT NULL,
    AttendanceDate  DATE                NOT NULL,
    Status          INT                 NOT NULL CONSTRAINT DF_Approval_Status DEFAULT (1),
    FirstCheckIn    DATETIME2           NULL,
    MinutesLate     INT                 NOT NULL CONSTRAINT DF_Approval_Late DEFAULT (0),
    DecidedBy       NVARCHAR(100)       NULL,
    DecidedDate     DATETIME2           NULL,
    Note            NVARCHAR(500)       NULL,
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_Approval_Created DEFAULT (GETDATE()),
    CONSTRAINT PK_AttendanceApprovals PRIMARY KEY (ApprovalId),
    CONSTRAINT FK_Approvals_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE CASCADE,
    CONSTRAINT CK_Approval_Status CHECK (Status IN (0, 1, 2, 3)),
    -- A decided row must record who decided it.
    CONSTRAINT CK_Approval_Decided
        CHECK (Status NOT IN (2, 3) OR (DecidedBy IS NOT NULL AND DecidedDate IS NOT NULL))
);
CREATE UNIQUE INDEX IX_AttendanceApproval_Employee_Date
    ON AttendanceApprovals (EmployeeId, AttendanceDate);
CREATE INDEX IX_AttendanceApproval_Status_Date
    ON AttendanceApprovals (Status, AttendanceDate);


-- Non-working days. BranchId NULL means the holiday applies everywhere.
-- Saturday is the fixed weekly off in Nepal and is computed, not stored.
CREATE TABLE Holidays (
    HolidayId           INT IDENTITY(1,1)   NOT NULL,
    HolidayName         NVARCHAR(100)       NOT NULL,
    HolidayDate         DATE                NOT NULL,
    DurationDays        INT                 NOT NULL CONSTRAINT DF_Holidays_Duration DEFAULT (1),
    Description         NVARCHAR(500)       NULL,
    HolidayType         NVARCHAR(20)        NULL,   -- Festival / Religious / National / Public / Company
    IsRecurring         BIT                 NOT NULL CONSTRAINT DF_Holidays_Recurring DEFAULT (0),
    IsWeeklyRecurring   BIT                 NOT NULL CONSTRAINT DF_Holidays_Weekly DEFAULT (0),
    RecurringDayOfWeek  INT                 NULL,   -- 0 = Sunday ... 6 = Saturday
    BranchId            INT                 NULL,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Holidays_IsActive DEFAULT (1),
    CreatedDate         DATETIME2           NOT NULL CONSTRAINT DF_Holidays_Created DEFAULT (GETDATE()),
    ModifiedDate        DATETIME2           NULL,
    CONSTRAINT PK_Holidays PRIMARY KEY (HolidayId),
    CONSTRAINT FK_Holidays_Branch FOREIGN KEY (BranchId)
        REFERENCES Branches (BranchId) ON DELETE SET NULL,
    CONSTRAINT CK_Holidays_Duration CHECK (DurationDays BETWEEN 1 AND 30),
    CONSTRAINT CK_Holidays_DayOfWeek
        CHECK (RecurringDayOfWeek IS NULL OR RecurringDayOfWeek BETWEEN 0 AND 6)
);
CREATE UNIQUE INDEX IX_Holiday_NameDate ON Holidays (HolidayName, HolidayDate);
CREATE INDEX IX_Holiday_Date ON Holidays (HolidayDate) WHERE IsActive = 1;


/* ---------------------------------------------------------------------------
   5. SECURITY
   --------------------------------------------------------------------------- */

CREATE TABLE ApiUsers (
    ApiUserId       INT IDENTITY(1,1)   NOT NULL,
    Username        NVARCHAR(50)        NOT NULL,
    Email           NVARCHAR(150)       NOT NULL,
    PasswordHash    NVARCHAR(200)       NOT NULL,
    PasswordSalt    NVARCHAR(100)       NOT NULL,
    Role            NVARCHAR(20)        NOT NULL CONSTRAINT DF_ApiUsers_Role DEFAULT ('Employee'),
    EmployeeId      INT                 NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_ApiUsers_IsActive DEFAULT (1),
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_ApiUsers_Created DEFAULT (GETDATE()),
    LastLoginDate   DATETIME2           NULL,
    CONSTRAINT PK_ApiUsers PRIMARY KEY (ApiUserId),
    CONSTRAINT FK_ApiUsers_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees (EmployeeId) ON DELETE SET NULL,
    CONSTRAINT CK_ApiUsers_Role CHECK (Role IN ('Admin', 'Manager', 'Employee'))
);
CREATE UNIQUE INDEX UX_ApiUser_Username ON ApiUsers (Username);
CREATE UNIQUE INDEX UX_ApiUser_Email ON ApiUsers (Email);
-- One login per employee. Filtered so the many NULLs do not collide.
CREATE UNIQUE INDEX UX_ApiUser_Employee ON ApiUsers (EmployeeId)
    WHERE EmployeeId IS NOT NULL;


CREATE TABLE RefreshTokens (
    RefreshTokenId  INT IDENTITY(1,1)   NOT NULL,
    ApiUserId       INT                 NOT NULL,
    Token           NVARCHAR(200)       NOT NULL,
    ExpiresAt       DATETIME2           NOT NULL,
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_RefreshTokens_Created DEFAULT (GETDATE()),
    RevokedAt       DATETIME2           NULL,
    CONSTRAINT PK_RefreshTokens PRIMARY KEY (RefreshTokenId),
    CONSTRAINT FK_RefreshTokens_User FOREIGN KEY (ApiUserId)
        REFERENCES ApiUsers (ApiUserId) ON DELETE CASCADE
);
CREATE UNIQUE INDEX UX_RefreshToken_Token ON RefreshTokens (Token);


/* ---------------------------------------------------------------------------
   6. SYSTEM
   --------------------------------------------------------------------------- */

-- Key/value configuration. Category 'Attendance' holds the office-hours rules
-- edited from the Settings screen, so changing them needs no redeploy.
CREATE TABLE SystemSettings (
    SettingId       INT IDENTITY(1,1)   NOT NULL,
    SettingKey      NVARCHAR(100)       NOT NULL,
    SettingValue    NVARCHAR(500)       NOT NULL,
    Category        NVARCHAR(50)        NULL,
    Description     NVARCHAR(500)       NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_SystemSettings_IsActive DEFAULT (1),
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_SystemSettings_Created DEFAULT (GETDATE()),
    ModifiedDate    DATETIME2           NULL,
    CONSTRAINT PK_SystemSettings PRIMARY KEY (SettingId)
);
CREATE UNIQUE INDEX IX_SystemSetting_Key ON SystemSettings (SettingKey);


CREATE TABLE Notifications (
    NotificationId  BIGINT IDENTITY(1,1) NOT NULL,
    RecipientUserId INT                 NULL,
    RecipientRole   NVARCHAR(20)        NULL,
    Message         NVARCHAR(300)       NOT NULL,
    LinkPath        NVARCHAR(200)       NULL,
    Type            NVARCHAR(40)        NULL,
    IsRead          BIT                 NOT NULL CONSTRAINT DF_Notifications_Read DEFAULT (0),
    CreatedDate     DATETIME2           NOT NULL CONSTRAINT DF_Notifications_Created DEFAULT (GETDATE()),
    CONSTRAINT PK_Notifications PRIMARY KEY (NotificationId),
    CONSTRAINT FK_Notifications_User FOREIGN KEY (RecipientUserId)
        REFERENCES ApiUsers (ApiUserId) ON DELETE CASCADE,
    -- Addressed to a person or to a role, never to neither.
    CONSTRAINT CK_Notifications_Target
        CHECK (RecipientUserId IS NOT NULL OR RecipientRole IS NOT NULL)
);
CREATE INDEX IX_Notification_Recipient ON Notifications (RecipientUserId, IsRead);


/* ---------------------------------------------------------------------------
   SEED: attendance policy defaults (10:00 start, 15 min grace, 11:00 cut-off)
   --------------------------------------------------------------------------- */
INSERT INTO SystemSettings (SettingKey, SettingValue, Category, Description) VALUES
 ('OfficeStartTime',              '10:00', 'Attendance', 'Working day start time'),
 ('OfficeEndTime',                '18:00', 'Attendance', 'Working day end time'),
 ('GraceMinutes',                 '15',    'Attendance', 'Minutes after start that still count as on time'),
 ('ApprovalRequiredAfterMinutes', '60',    'Attendance', 'Minutes after start beyond which an admin must approve'),
 ('RequireApprovalForLate',       'false', 'Attendance', 'Also require approval between grace and the cut-off'),
 ('CloseGraceMinutes',            '30',    'Attendance', 'Minutes after end time before a no-show is marked absent'),
 ('HalfDayUnderHours',            '4',     'Attendance', 'Hours below which a present day counts as a half day');
GO
