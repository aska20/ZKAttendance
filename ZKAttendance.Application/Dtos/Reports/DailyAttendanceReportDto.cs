namespace ZKAttendance.Application.Dtos.Reports
{
    /// <summary>
    /// The complete daily attendance report, used by ReportService
    /// </summary>
    public class DailyAttendanceReportDto
    {
        /// <summary>
        /// Report date
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Attendance rows (present and absent)
        /// </summary>
        public List<DailyAttendanceItemDto> Items { get; set; } = new();

        /// <summary>
        /// Total employee count
        /// </summary>
        public int TotalEmployees { get; set; }

        /// <summary>
        /// Number present
        /// </summary>
        public int PresentCount { get; set; }

        /// <summary>
        /// Number absent
        /// </summary>
        public int AbsentCount { get; set; }

        /// <summary>
        /// Attendance percentage
        /// </summary>
        public double AttendanceRate => TotalEmployees > 0
            ? Math.Round((double)PresentCount / TotalEmployees * 100, 2)
            : 0;
    }
}
