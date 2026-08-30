using ZKAttendance.Domain.Entities;
namespace ZKAttendance.Application.Dtos.Reports
{

    /// <summary>
    /// DTO for one employee's attendance on one day
    /// </summary>
    public class DailyAttendanceItemDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string BiometricUserId { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateTime? FirstCheckIn { get; set; }
        public DateTime? LastCheckOut { get; set; }
        public TimeSpan? TotalWorkHours { get; set; }
        public string Status { get; set; } = "Absent";

        /// <summary>
        /// Working hours formatted for display (e.g. 8:30)
        /// </summary>
        public string TotalWorkHoursFormatted => TotalWorkHours.HasValue
            ? $"{(int)TotalWorkHours.Value.TotalHours}:{TotalWorkHours.Value.Minutes:D2}"
            : "--";
    }
}
