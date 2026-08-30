using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Devices
{
    public class DeviceMonitorBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DeviceMonitorBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // every minute

        public DeviceMonitorBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<DeviceMonitorBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Device monitor started - checking every {Interval} minute(s)",
                _checkInterval.TotalMinutes);

            // Wait 10 seconds so the app finishes starting
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            // PeriodicTimer, available in .NET 6+
            using var timer = new PeriodicTimer(_checkInterval);

            try
            {
                // Run the first check immediately
                await RunCheckAsync();

                // then every minute
                while (!stoppingToken.IsCancellationRequested
                       && await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await RunCheckAsync();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Device monitor stopped");
            }
        }

        private async Task RunCheckAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var monitorService = scope.ServiceProvider.GetRequiredService<IDeviceMonitorService>();

                await monitorService.CheckDevicesStatusAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while running the device check");
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping the device monitor...");
            return base.StopAsync(cancellationToken);
        }
    }
}
