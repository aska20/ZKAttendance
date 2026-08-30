using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Services.Devices;

namespace ZKAttendance.Infrastructure.Devices
{
    /// <summary>
    /// Runs a sync round on a timer.
    ///
    /// This sits alongside your existing DeviceMonitorBackgroundService, which
    /// keeps doing what it already does - a cheap ping every minute so the
    /// dashboard can show green/red dots. This one does the expensive work of
    /// actually opening an SDK session and pulling records, so it runs less
    /// often (default every 5 minutes, matching SyncConfiguration in
    /// appsettings.json).
    ///
    /// Register in Program.cs:
    ///
    ///   builder.Services.AddScoped&lt;IAttendanceSyncService, AttendanceSyncService&gt;();
    ///   builder.Services.AddTransient&lt;Func&lt;IZkDeviceReader&gt;&gt;(sp =&gt;
    ///       () =&gt; new ZkemkeeperDeviceReader(
    ///                 sp.GetRequiredService&lt;ILogger&lt;ZkemkeeperDeviceReader&gt;&gt;()));
    ///   builder.Services.AddHostedService&lt;AttendanceSyncBackgroundService&gt;();
    /// </summary>
    public class AttendanceSyncBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly ILogger<AttendanceSyncBackgroundService> _logger;

        public AttendanceSyncBackgroundService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<AttendanceSyncBackgroundService> logger)
        {
            _services = services;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _config.GetValue("SyncConfiguration:EnableAutoSync", true);
            if (!enabled)
            {
                _logger.LogInformation("Automatic attendance sync is disabled in configuration");
                return;
            }

            var minutes = _config.GetValue("SyncConfiguration:SyncIntervalMinutes", 5);
            var interval = TimeSpan.FromMinutes(minutes);

            _logger.LogInformation("Attendance sync service started - every {Minutes} minute(s)", minutes);

            // Let the app finish starting before touching the network.
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

            using var timer = new PeriodicTimer(interval);

            try
            {
                await RunOnceAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested
                       && await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await RunOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Attendance sync service stopping");
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _services.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<IAttendanceSyncService>();
                await sync.SyncAllDevicesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Never let a bad round kill the loop - the next tick should still fire.
                _logger.LogError(ex, "Sync round failed");
            }
        }
    }
}
