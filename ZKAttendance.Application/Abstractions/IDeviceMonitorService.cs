namespace ZKAttendance.Application.Abstractions
{
    public interface IDeviceMonitorService
    {
        Task CheckDevicesStatusAsync();
    }
}
