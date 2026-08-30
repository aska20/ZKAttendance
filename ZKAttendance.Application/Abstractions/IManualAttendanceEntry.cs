using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    /// <summary>
    /// Insert a punch by hand, for when someone worked but the device has no
    /// record — a failed scan, or a machine that was down.
    ///
    /// Deliberately separate from the device sync path so manual rows are
    /// always flagged and never mistaken for hardware data during an audit.
    /// </summary>
    public interface IManualAttendanceEntry
    {
        Task<AttendanceLog> AddAsync(
            int employeeId,
            int deviceId,
            DateTime punchTime,
            string attendanceType,
            string? notes,
            CancellationToken ct = default);
    }
}
