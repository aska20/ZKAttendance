using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;
using ZKAttendance.Infrastructure.Persistence;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Devices
{
    public class DeviceMonitorService : IDeviceMonitorService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<DeviceMonitorService> _logger;

        public DeviceMonitorService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<DeviceMonitorService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        public async Task CheckDevicesStatusAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

                var devices = await context.Devices.ToListAsync();

                _logger.LogInformation("Checking {Count} device(s)...", devices.Count);

                foreach (var device in devices)
                {
                    try
                    {
                        // Check the device connection
                        var isOnline = await CheckDeviceConnectionAsync(device.DeviceIP, device.DevicePort);

                        // Update the status
                        device.IsOnline = isOnline;
                        device.LastCheckTime = DateTime.Now;
                        device.ConnectionStatus = isOnline ? "Connected" : "Disconnected";

                        if (isOnline)
                        {
                            device.LastConnectionTime = DateTime.Now;
                        }

                        _logger.LogInformation(
                            "{Status} Device: {DeviceName} ({IP}:{Port})",
                            isOnline ? "Online" : "Offline",
                            device.DeviceName,
                            device.DeviceIP,
                            device.DevicePort
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking device {DeviceName}", device.DeviceName);
                        device.IsOnline = false;
                        device.LastCheckTime = DateTime.Now;
                        device.ConnectionStatus = "Error";
                    }
                }

                await context.SaveChangesAsync();
                _logger.LogInformation("All device statuses updated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Device monitor service error");
            }
        }

        /// <summary>
        /// Check the device using ping and a TCP probe
        /// </summary>
        private async Task<bool> CheckDeviceConnectionAsync(string ipAddress, int port)
        {
            // Step 1: ping
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, 2000); // 2 second timeout

                if (reply.Status == IPStatus.Success)
                {
                    // Step 2: probe the port
                    return await CheckTcpPortAsync(ipAddress, port);
                }
            }
            catch
            {
                // If ping failed, try TCP directly
            }

            // Ping failed, so try TCP only
            return await CheckTcpPortAsync(ipAddress, port);
        }

        /// <summary>
        /// Check whether the port is open on the device
        /// </summary>
        private async Task<bool> CheckTcpPortAsync(string ipAddress, int port)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(3000); // 3 second timeout

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch
            {
                // Connection failed
            }

            return false;
        }
    }
}
