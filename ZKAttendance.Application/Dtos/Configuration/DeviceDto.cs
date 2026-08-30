namespace ZKAttendance.Application.Dtos.Configuration
{
    /// <summary>
    /// DTO for device information
    /// </summary>
    public class DeviceDto
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceIP { get; set; } = string.Empty;
        public int DevicePort { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
