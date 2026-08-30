using ZKAttendance.Domain.Entities;
namespace ZKAttendance.Application.Dtos.Configuration
{
    /// <summary>
    /// DTO for a branch's full configuration including devices
    /// Used by ConfigurationController
    /// </summary>
    public class BranchConfigurationDto
    {
        public BranchDto Branch { get; set; } = new();
        public List<DeviceDto> Devices { get; set; } = new();
    }

    

}
