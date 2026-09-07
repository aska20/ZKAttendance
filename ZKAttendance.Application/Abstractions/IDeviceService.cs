using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    public interface IDeviceService
    {
        Task<List<Device>> GetAllDevicesAsync(bool includeInactive = false);
        Task<Device?> GetDeviceByIdAsync(int deviceId);
        Task<List<Device>> GetDevicesByBranchIdAsync(int branchId);
        Task<Device> CreateDeviceAsync(Device device);
        Task<Device> UpdateDeviceAsync(Device device);
        Task DeleteDeviceAsync(int deviceId);
        Task<(bool Success, string Message)> TestDeviceConnectionAsync(int deviceId);
        Task<List<Device>> GetOnlineDevicesAsync();
        Task<List<Device>> GetOfflineDevicesAsync();
    }
}
