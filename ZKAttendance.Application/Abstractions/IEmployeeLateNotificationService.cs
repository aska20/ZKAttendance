using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZKAttendance.Application.Abstractions
{
    public interface IEmployeeLateNotificationService
    {
        /// <summary>
        /// Evaluates all first-arrival punches for the specified date against attendance policy,
        /// and sends late arrival notifications to any employee arriving past grace who has not yet been notified today.
        /// </summary>
        /// <param name="date">Attendance date to evaluate (usually DateTime.Today).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Number of late notifications sent/enqueued.</returns>
        Task<int> ProcessDailyLateArrivalsAsync(DateTime date, CancellationToken ct = default);

        /// <summary>
        /// Evaluates today's attendance punches against policy. Safe for Hangfire recurring schedules.
        /// </summary>
        Task<int> ProcessTodayLateArrivalsAsync(CancellationToken ct = default);

        /// <summary>
        /// Dispatches a late arrival alert (in-app notification and email if available) to an employee.
        /// </summary>
        Task SendLateNotificationToEmployeeAsync(
            int employeeId,
            DateTime attendanceDate,
            TimeSpan checkInTime,
            int minutesLate,
            CancellationToken ct = default);
    }
}
