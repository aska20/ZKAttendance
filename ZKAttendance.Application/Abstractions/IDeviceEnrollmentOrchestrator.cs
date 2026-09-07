namespace ZKAttendance.Application.Abstractions
{
    /// <summary>Where one employee stands on one terminal.</summary>
    public record DeviceEnrollmentState(
        int DeviceId,
        string DeviceName,
        string Role,
        bool IsActive,
        bool Reachable,
        /// <summary>The enrol number THIS device knows them by.</summary>
        string DeviceUserId,
        /// <summary>The user record exists on the terminal.</summary>
        bool UserOnDevice,
        /// <summary>How many fingers are enrolled on this terminal.</summary>
        int TemplateCount,
        string? Message);

    /// <summary>Result of asking a terminal to capture a finger.</summary>
    public record StartEnrollmentResult(
        bool Started,
        int DeviceId,
        string DeviceName,
        string DeviceUserId,
        int FingerIndex,
        string Message);

    /// <summary>What a pull-and-push round did.</summary>
    public record PropagationReport(
        int EmployeeId,
        string EmployeeName,
        int TemplatesCached,
        int DevicesUpdated,
        int DevicesFailed,
        IReadOnlyList<DeviceEnrollmentState> Devices,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Keeps every terminal agreeing on who exists and whose finger is whose.
    ///
    /// THE PROBLEM THIS SOLVES
    /// -----------------------
    /// Adding a person in this application only ever wrote a database row. The
    /// terminal at the door had never heard of them, so their first scan either
    /// failed or arrived as an unattributed punch. Meanwhile a second terminal
    /// at a branch had its own separate idea of who was enrolled, and nothing
    /// reconciled the two.
    ///
    /// THE MODEL
    /// ---------
    /// One ACTIVE device is the Master. It is the only place a finger is
    /// physically captured, because that is a hardware act — somebody has to
    /// stand there. Everything else is a Slave and receives its users by being
    /// written to from here.
    ///
    ///     add employee  ->  PushUserToDevicesAsync   (user record on every terminal)
    ///                   ->  StartEnrollmentAsync     (master switches to "place finger")
    ///                   ->  PullAndPropagateAsync    (template cached, then copied out)
    ///
    /// Caching the template in FingerprintTemplates is what makes a third or
    /// fourth terminal a background job rather than a day of calling everybody
    /// back to the sensor. It is also the only protection against the master
    /// dying or having its memory cleared.
    /// </summary>
    public interface IDeviceEnrollmentOrchestrator
    {
        /// <summary>Which terminals this employee is on, and how far enrolment got.</summary>
        Task<IReadOnlyList<DeviceEnrollmentState>> GetStatusAsync(
            int employeeId, CancellationToken ct = default);

        /// <summary>
        /// Create or refresh the employee's user record on every active
        /// terminal (or just one). Safe to call repeatedly — writing the same
        /// user twice overwrites rather than duplicates.
        /// </summary>
        Task<PropagationReport> PushUserToDevicesAsync(
            int employeeId, int? deviceId = null, CancellationToken ct = default);

        /// <summary>
        /// Put a terminal into capture mode for this employee. Defaults to the
        /// master. Returns once the terminal has accepted the command; the
        /// person then scans, and the template is collected by
        /// <see cref="PullAndPropagateAsync"/>.
        /// </summary>
        Task<StartEnrollmentResult> StartEnrollmentAsync(
            int employeeId, int? deviceId = null, int fingerIndex = 0, CancellationToken ct = default);

        /// <summary>Stop a terminal waiting on a scan.</summary>
        Task<bool> CancelEnrollmentAsync(
            int employeeId, int? deviceId = null, CancellationToken ct = default);

        /// <summary>
        /// Read whatever the master now holds for this employee, cache it, and
        /// write it to every other active terminal.
        /// </summary>
        Task<PropagationReport> PullAndPropagateAsync(
            int employeeId, CancellationToken ct = default);

        /// <summary>
        /// Fill a newly registered terminal from the cached templates of every
        /// active employee. Returns how many employees were written.
        /// </summary>
        Task<int> ProvisionDeviceAsync(int deviceId, CancellationToken ct = default);

        /// <summary>
        /// Sweep every employee who has cached templates but is missing from
        /// one or more terminals. This is what the background service calls,
        /// and what makes the devices self-heal after one has been offline.
        /// </summary>
        Task<int> ReconcileAllAsync(CancellationToken ct = default);
    }
}
