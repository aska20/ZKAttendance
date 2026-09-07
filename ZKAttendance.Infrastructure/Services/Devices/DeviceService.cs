using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Devices
{
    public class DeviceService : IDeviceService
    {
        private readonly DeviceRepository _repository;
        private readonly AttendanceDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DeviceService> _logger;

        public DeviceService(
            DeviceRepository repository,
            AttendanceDbContext context,
            IMemoryCache cache,
            ILogger<DeviceService> logger)
        {
            _repository = repository;
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<Device>> GetAllDevicesAsync(bool includeInactive = false)
        {
            try
            {
                return await _repository.GetAllAsync(includeInactive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the device list");
                throw;
            }
        }

        public async Task<Device?> GetDeviceByIdAsync(int deviceId)
        {
            try
            {
                return await _repository.GetByIdAsync(deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<List<Device>> GetDevicesByBranchIdAsync(int branchId)
        {
            try
            {
                return await _repository.GetByBranchIdAsync(branchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching branch devices {BranchId}", branchId);
                throw;
            }
        }

        public async Task<Device> CreateDeviceAsync(Device device)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Reject a duplicate IP + port
                if (await _repository.IpPortExistsAsync(device.DeviceIP, device.DevicePort))
                {
                    throw new InvalidOperationException(
                        $"Another device already uses the same IP address ({device.DeviceIP}:{device.DevicePort})");
                }

                device.IsActive = true;
                device.CreatedDate = DateTime.Now;

                var createdDevice = await _repository.AddAsync(device);

                // Create the initial DeviceStatus row
                var initialStatus = new DeviceStatus
                {
                    DeviceId = createdDevice.DeviceId,
                    BranchId = createdDevice.BranchId,
                    IsOnline = false,
                    StatusTime = DateTime.Now,
                    LastUpdateTime = DateTime.Now,
                    CreatedDate = DateTime.Now,
                    StatusMessage = "Device created"
                };

                _context.DeviceStatuses.Add(initialStatus);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                ClearDeviceCache();
                _logger.LogInformation("Created device: {DeviceName}", device.DeviceName);

                return createdDevice;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating device: {DeviceName}", device.DeviceName);
                throw;
            }
        }

        public async Task<Device> UpdateDeviceAsync(Device device)
        {
            try
            {
                if (!await _repository.ExistsAsync(device.DeviceId))
                {
                    throw new InvalidOperationException("Device not found");
                }

                // Reject a duplicate IP + port
                if (await _repository.IpPortExistsAsync(device.DeviceIP, device.DevicePort, device.DeviceId))
                {
                    throw new InvalidOperationException(
                        $"Another device already uses the same IP address ({device.DeviceIP}:{device.DevicePort})");
                }

                device.ModifiedDate = DateTime.Now;
                var result = await _repository.UpdateAsync(device);

                ClearDeviceCache();
                _logger.LogInformation("Updated device: {DeviceName}", device.DeviceName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating device {DeviceId}", device.DeviceId);
                throw;
            }
        }

        public async Task DeleteDeviceAsync(int deviceId)
        {
            try
            {
                await _repository.SoftDeleteAsync(deviceId);
                ClearDeviceCache();
                _logger.LogInformation("Device deleted {DeviceId}", deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<(bool Success, string Message)> TestDeviceConnectionAsync(int deviceId)
        {
            try
            {
                var device = await _repository.GetByIdAsync(deviceId);
                if (device == null)
                {
                    return (false, "Device not found");
                }

                // Real device connection logic goes here
                // for example using zkemkeeper.dll
                // var zkDevice = new zkemkeeper.CZKEMClass();
                // bool connected = zkDevice.Connect_Net(device.DeviceIP, device.DevicePort);

                // Simulated for now
                await _repository.UpdateConnectionStatusAsync(
                    deviceId,
                    true,
                    "Connected successfully (simulated)"
                );

                return (true, "Connected to the device successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing the device connection {DeviceId}", deviceId);
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        public async Task<List<Device>> GetOnlineDevicesAsync()
        {
            try
            {
                return await _repository.GetOnlineDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching online devices");
                throw;
            }
        }

        public async Task<List<Device>> GetOfflineDevicesAsync()
        {
            try
            {
                return await _repository.GetOfflineDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching offline devices");
                throw;
            }
        }

        private void ClearDeviceCache()
        {
            _cache.Remove("active_devices");
            _cache.Remove("devices");
        }
    }
}
