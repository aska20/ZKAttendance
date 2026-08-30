namespace ZKAttendance.Application.Abstractions
{
    /// <summary>One employee-to-device link, as returned to a caller.</summary>
    public record DeviceAssignment(int DeviceId, string DeviceUserId, bool IsEnrolled);

    /// <summary>
    /// Records which biometric devices an employee exists on, and the enrol
    /// number each device knows them by.
    ///
    /// WHY THIS IS SEPARATE FROM IEmployeeService
    /// ------------------------------------------
    /// IEmployeeService deals with the person. This deals with the pairing of a
    /// person and a machine, which is a different thing with its own rule: an
    /// enrol number is unique per device, not globally. The same number can
    /// belong to different people on different machines, which is exactly what
    /// happens when each machine allocates its own numbers.
    /// </summary>
    public interface IEmployeeDeviceEnrollment
    {
        /// <summary>
        /// Replace this employee's device links with the given set.
        ///
        /// <paramref name="deviceUserIds"/> optionally supplies a per-device
        /// enrol number. A device left out inherits the employee's primary
        /// biometric ID, unless a number was already recorded for it - in which
        /// case the existing one is kept, so re-saving does not silently wipe a
        /// branch-specific number.
        /// </summary>
        Task<IReadOnlyList<DeviceAssignment>> AssignAsync(
            int employeeId,
            IEnumerable<int> deviceIds,
            IDictionary<int, string>? deviceUserIds = null,
            CancellationToken ct = default);

        Task<IReadOnlyList<DeviceAssignment>> GetForEmployeeAsync(
            int employeeId, CancellationToken ct = default);
    }
}
