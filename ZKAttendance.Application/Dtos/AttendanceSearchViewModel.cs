using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Dtos
{
    public class AttendanceSearchViewModel
    {
        public string? SearchTerm { get; set; }
        public int? BranchId { get; set; }
        public String? EmployeeName { get; set;}
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public List<AttendanceLog> AttendanceLogs { get; set; } = new();
        public List<Branch> Branches { get; set; } = new();
        public Dictionary<string, Employee?> EmployeeCache { get; set; } = new();
    }
}
