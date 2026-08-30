using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Dtos
{
    public class EmployeeDetailsViewModel
    {
        public Employee Employee { get; set; } = new();

        // Only the linked branches and devices
        public List<BranchWithDevicesViewModel> AssignedBranches { get; set; } = new();

        // Nested classes
        public class BranchWithDevicesViewModel
        {
            public int BranchId { get; set; }
            public string BranchCode { get; set; } = string.Empty;
            public string BranchName { get; set; } = string.Empty;
            public string? City { get; set; }

            public List<DeviceInfoViewModel> Devices { get; set; } = new();
        }

        public class DeviceInfoViewModel
        {
            public int DeviceId { get; set; }
            public string DeviceName { get; set; } = string.Empty;
            public string DeviceIP { get; set; } = string.Empty;
            public string? SerialNumber { get; set; }
        }
    }
}
