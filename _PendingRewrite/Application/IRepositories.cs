using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions;

public interface IDeviceRepository
{
    Task<Device?> GetAsync(int deviceId, CancellationToken ct = default);

    /// <summary>
    /// The single enrolment device. Returns null when none is configured, and
    /// the enrolment pull is skipped with a clear log message rather than
    /// guessing which machine to trust.
    /// </summary>
    Task<Device?> GetMasterAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Device>> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Device>> GetActiveSlavesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Device>> GetUnprovisionedAsync(CancellationToken ct = default);
}

public interface IEmployeeRepository
{
    Task<Employee?> GetAsync(int employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetActiveAsync(CancellationToken ct = default);
    Task AddAsync(Employee employee, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string employeeCode, CancellationToken ct = default);
}

public interface IMappingRepository
{
    /// <summary>
    /// DeviceUserId -> EmployeeId for one device.
    ///
    /// Scoped to a device on purpose. A dictionary keyed by DeviceUserId alone
    /// would be wrong, because the same number belongs to different people on
    /// different machines.
    /// </summary>
    Task<Dictionary<string, int>> GetLookupForDeviceAsync(int deviceId, CancellationToken ct = default);

    Task<EmployeeDeviceMapping?> FindAsync(int employeeId, int deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDeviceMapping>> GetForEmployeeAsync(int employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDeviceMapping>> GetPendingTemplateSyncAsync(CancellationToken ct = default);

    /// <summary>Every DeviceUserId already taken on this device, for allocating a free one.</summary>
    Task<HashSet<string>> GetUsedDeviceUserIdsAsync(int deviceId, CancellationToken ct = default);

    Task AddAsync(EmployeeDeviceMapping mapping, CancellationToken ct = default);
}

public interface ITemplateRepository
{
    Task<IReadOnlyList<FingerprintTemplate>> GetForEmployeeAsync(int employeeId, CancellationToken ct = default);
    Task ReplaceForEmployeeAsync(int employeeId, IEnumerable<FingerprintTemplate> templates, CancellationToken ct = default);
    Task<bool> HasTemplatesAsync(int employeeId, CancellationToken ct = default);
}

public interface IPendingEnrollmentRepository
{
    Task<PendingEnrollment?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PendingEnrollment>> GetAwaitingReviewAsync(CancellationToken ct = default);

    /// <summary>
    /// Enrol numbers already seen on this device, whatever their status.
    /// Includes rejected ones, so a technician's test enrolment is not
    /// re-raised on every pull.
    /// </summary>
    Task<HashSet<string>> GetKnownDeviceUserIdsAsync(int deviceId, CancellationToken ct = default);

    Task AddAsync(PendingEnrollment pending, CancellationToken ct = default);
}

public interface IAttendanceLogRepository
{
    Task AddRangeAsync(IEnumerable<AttendanceLog> logs, CancellationToken ct = default);

    /// <summary>
    /// (DeviceUserId, PunchTime) pairs already stored for this device from
    /// <paramref name="from"/> onwards, used to skip repeats cheaply before
    /// the database has to reject them.
    /// </summary>
    Task<HashSet<(string DeviceUserId, DateTime PunchTime)>> GetExistingKeysAsync(
        int deviceId, DateTime from, CancellationToken ct = default);
}

public interface ISyncLogRepository
{
    Task AddAsync(DeviceSyncLog log, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default);

    /// <summary>True when the exception is a unique-index violation, not a real fault.</summary>
    bool IsUniqueViolation(Exception ex);
}

/// <summary>Wall clock, injected so tests can control time.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
    DateTime Today { get; }
}

/// <summary>Gregorian to Bikram Sambat, implemented in Infrastructure.</summary>
public interface INepaliCalendar
{
    string ToBsString(DateTime ad);
    (DateTime FromAd, DateTime ToAd) BsMonthRange(int bsYear, int bsMonth);
    (DateTime FromAd, DateTime ToAd) FiscalYearRange(int startBsYear);
    bool IsWeeklyOff(DateTime ad);
}
