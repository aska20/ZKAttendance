using ZKAttendance.Application.Dtos.Reports;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Dtos
{
    public class DailyReportViewModel
    {
        public DateTime SelectedDate { get; set; } = DateTime.Today;
        public string? Department { get; set; }
        public DailyAttendanceReportDto? Report { get; set; }
        public List<string> AvailableDepartments { get; set; } = new List<string>();
    }
}
