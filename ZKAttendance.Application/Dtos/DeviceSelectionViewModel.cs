namespace ZKAttendance.Application.Dtos
{
    public class DeviceSelectionViewModel
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceIP { get; set; } = string.Empty;
        public string? SerialNumber { get; set; }
        public bool IsSelected { get; set; }
    }
}
