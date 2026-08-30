using System;
using System.Collections.Generic;
using ZKAttendance.Application.Dtos.Reports;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Dtos
{
    public class DailyReportFilterViewModel
    {
        // Filter options
        public int? SelectedBranchId { get; set; }
        public int? SelectedDeviceId { get; set; }
        public int? SelectedEmployeeId { get; set; }

        public DateTime DateFrom { get; set; } = DateTime.Today.AddDays(-7);
        public DateTime DateTo { get; set; } = DateTime.Today;

        // Dropdown lists
        public List<Branch> Branches { get; set; } = new List<Branch>();
        public List<Device> Devices { get; set; } = new List<Device>();
        public List<Employee> Employees { get; set; } = new List<Employee>();

        // The displayed result
        public DailyAttendanceReportDto? Report { get; set; }
    }
}
