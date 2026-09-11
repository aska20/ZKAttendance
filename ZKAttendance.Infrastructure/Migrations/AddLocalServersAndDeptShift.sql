/* ============================================================================
   Manual alternative to:
       Add-Migration AddLocalServersAndDeptShift
       Update-Database

   Prefer the EF route. Use this only if migrations will not run.
   Safe to re-run.

   WHY YOU NEED THIS
   -----------------
   Device.LocalServerId and Department.DefaultShiftId were added to the entities.
   EF selects those columns on every device and department query, so until they
   exist SQL Server answers "Invalid column name" and the Devices, Employees and
   Overview screens all fail at once.
   ============================================================================ */

-- 1. Devices.LocalServerId
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Devices') AND name = 'LocalServerId')
BEGIN
    ALTER TABLE dbo.Devices ADD LocalServerId INT NULL;
    PRINT 'Added Devices.LocalServerId';
END
ELSE PRINT 'Devices.LocalServerId already exists';
GO

-- 2. Departments.DefaultShiftId
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Departments') AND name = 'DefaultShiftId')
BEGIN
    ALTER TABLE dbo.Departments ADD DefaultShiftId INT NULL;
    ALTER TABLE dbo.Departments ADD CONSTRAINT FK_Departments_WorkShifts_DefaultShiftId
        FOREIGN KEY (DefaultShiftId) REFERENCES dbo.WorkShifts(ShiftId) ON DELETE SET NULL;
    PRINT 'Added Departments.DefaultShiftId';
END
ELSE PRINT 'Departments.DefaultShiftId already exists';
GO

-- 3. LocalServers
IF OBJECT_ID('dbo.LocalServers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalServers (
        LocalServerId    INT IDENTITY(1,1) NOT NULL,
        ServerName       NVARCHAR(100) NOT NULL,
        AgentKey         VARCHAR(64)   NOT NULL,
        SecretHash       NVARCHAR(200) NOT NULL,
        SecretSalt       NVARCHAR(100) NOT NULL,
        BranchId         INT           NOT NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_LocalServer_Active DEFAULT (1),
        IsConnected      BIT           NOT NULL CONSTRAINT DF_LocalServer_Conn   DEFAULT (0),
        LastConnectedAt  DATETIME2     NULL,
        LastHeartbeatAt  DATETIME2     NULL,
        LastImportAt     DATETIME2     NULL,
        LastRemoteIp     NVARCHAR(45)  NULL,
        AgentVersion     NVARCHAR(50)  NULL,
        RecordsImported  BIGINT        NOT NULL CONSTRAINT DF_LocalServer_Rec    DEFAULT (0),
        CreatedDate      DATETIME2     NOT NULL CONSTRAINT DF_LocalServer_Created DEFAULT (GETDATE()),
        ModifiedDate     DATETIME2     NULL,
        CONSTRAINT PK_LocalServers PRIMARY KEY (LocalServerId),
        CONSTRAINT FK_LocalServers_Branches_BranchId
            FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId)
    );

    -- The agent key is the identity on the wire, so it is unique system-wide.
    CREATE UNIQUE INDEX UX_LocalServer_AgentKey ON dbo.LocalServers (AgentKey);
    CREATE INDEX IX_LocalServer_Branch ON dbo.LocalServers (BranchId);

    -- SetNull, not cascade: deleting an agent must never delete the devices
    -- behind it. They simply go back to being polled directly.
    ALTER TABLE dbo.Devices ADD CONSTRAINT FK_Devices_LocalServers_LocalServerId
        FOREIGN KEY (LocalServerId) REFERENCES dbo.LocalServers(LocalServerId) ON DELETE SET NULL;

    PRINT 'Created LocalServers';
END
ELSE PRINT 'LocalServers already exists';
GO

-- 4. Verify. All four must say OK.
SELECT
  CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Devices') AND name='LocalServerId')
       THEN 'OK' ELSE 'MISSING' END AS [Devices.LocalServerId],
  CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Departments') AND name='DefaultShiftId')
       THEN 'OK' ELSE 'MISSING' END AS [Departments.DefaultShiftId],
  CASE WHEN OBJECT_ID('dbo.LocalServers','U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS [LocalServers],
  CASE WHEN OBJECT_ID('dbo.AttendanceApprovals','U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS [AttendanceApprovals];
GO
