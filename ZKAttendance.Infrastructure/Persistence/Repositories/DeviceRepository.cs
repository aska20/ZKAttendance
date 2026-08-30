using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Persistence.Repositories
{
    public class DeviceRepository
    {
        private readonly AttendanceDbContext _context;

        public DeviceRepository(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<List<Device>> GetAllAsync(bool includeInactive = false)
        {
            var query = _context.Devices
                .Include(d => d.Branch)
                .AsQueryable();

            if (!includeInactive)
                query = query.Where(d => d.IsActive);

            return await query.OrderBy(d => d.DeviceName).ToListAsync();
        }

        public async Task<Device?> GetByIdAsync(int deviceId)
        {
            return await _context.Devices
                .Include(d => d.Branch)
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        }

        public async Task<List<Device>> GetByBranchIdAsync(int branchId)
        {
            return await _context.Devices
                .Where(d => d.BranchId == branchId && d.IsActive)
                .OrderBy(d => d.DeviceName)
                .ToListAsync();
        }

        public async Task<Device?> GetByIpAndPortAsync(string ip, int port)
        {
            return await _context.Devices
                .FirstOrDefaultAsync(d => d.DeviceIP == ip && d.DevicePort == port);
        }

        public async Task<bool> IpPortExistsAsync(string ip, int port, int? excludeDeviceId = null)
        {
            var query = _context.Devices.Where(d => d.DeviceIP == ip && d.DevicePort == port);

            if (excludeDeviceId.HasValue)
                query = query.Where(d => d.DeviceId != excludeDeviceId.Value);

            return await query.AnyAsync();
        }

        public async Task<bool> ExistsAsync(int deviceId)
        {
            return await _context.Devices.AnyAsync(d => d.DeviceId == deviceId);
        }

        public async Task<Device> AddAsync(Device device)
        {
            _context.Devices.Add(device);
            await _context.SaveChangesAsync();
            return device;
        }

        public async Task<Device> UpdateAsync(Device device)
        {
            _context.Devices.Update(device);
            await _context.SaveChangesAsync();
            return device;
        }

        public async Task SoftDeleteAsync(int deviceId)
        {
            var device = await _context.Devices.FindAsync(deviceId);
            if (device != null)
            {
                device.IsActive = false;
                device.ModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<Device>> GetOnlineDevicesAsync()
        {
            return await _context.Devices
                .Where(d => d.IsActive && d.IsOnline)
                .Include(d => d.Branch)
                .ToListAsync();
        }

        public async Task<List<Device>> GetOfflineDevicesAsync()
        {
            return await _context.Devices
                .Where(d => d.IsActive && !d.IsOnline)
                .Include(d => d.Branch)
                .ToListAsync();
        }

        public async Task UpdateConnectionStatusAsync(int deviceId, bool isOnline, string? statusMessage = null)
        {
            var device = await _context.Devices.FindAsync(deviceId);
            if (device != null)
            {
                device.IsOnline = isOnline;
                device.LastCheckTime = DateTime.Now;
                device.ConnectionStatus = statusMessage;

                if (isOnline)
                    device.LastConnectionTime = DateTime.Now;

                await _context.SaveChangesAsync();
            }
        }
    }
}
