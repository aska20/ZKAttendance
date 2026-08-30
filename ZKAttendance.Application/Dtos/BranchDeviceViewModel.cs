namespace ZKAttendance.Application.Dtos
{
    public class BranchDeviceViewModel
    {
        public int BranchId { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool IsBranchSelected { get; set; }

       
    public List<DeviceSelectionViewModel> Devices { get; set; } = new();
    
}
}
