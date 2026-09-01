using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Devices
{
    /// <summary>
    /// Produces believable punches without any hardware.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// zkemkeeper.dll is a 32-bit COM component. It needs regsvr32, it forces
    /// the project to target x86, and it cannot run on a machine that has no
    /// device on the network. Without a fake, nothing about the sync pipeline
    /// can be developed, tested or demonstrated.
    ///
    /// With this class the whole flow works end to end: the background service
    /// wakes up, "reads" each device, resolves biometric IDs to employees,
    /// writes AttendanceLogs, and the attendance screen fills up.
    ///
    /// Switch it on and off in appsettings.json:
    ///     "SyncConfiguration": { "UseFakeDeviceReader": true }
    ///
    /// The generated punches are deterministic per device and per day, so the
    /// same day always produces the same data and re-running a sync correctly
    /// reports duplicates rather than inventing new records.
    /// </summary>
    public class FakeDeviceReader : IZkDeviceReader
    {
        private readonly ILogger<FakeDeviceReader> _logger;
        private string _ip = "";
        private bool _connected;

        /// <summary>Biometric IDs this fake device knows about.</summary>
        private static readonly string[] KnownUsers =
            { "1001", "1002", "1003", "1004", "1005", "1006", "1007", "1008" };

        public FakeDeviceReader(ILogger<FakeDeviceReader> logger) => _logger = logger;

        public Task<bool> ConnectAsync(string ip, int port, int commPassword = 0)
        {
            _ip = ip;
            _connected = true;
            _logger.LogInformation("FakeDeviceReader connected to {Ip}:{Port}", ip, port);
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            _connected = false;
            return Task.CompletedTask;
        }

        public Task<DeviceInfo?> GetDeviceInfoAsync()
        {
            if (!_connected) return Task.FromResult<DeviceInfo?>(null);

            return Task.FromResult<DeviceInfo?>(new DeviceInfo(
                SerialNumber: "FAKE-" + _ip.Replace(".", ""),
                FirmwareVersion: "Ver 6.60 (simulated)",
                UserCount: KnownUsers.Length,
                LogCount: 0,
                DeviceTime: DateTime.Now.AddSeconds(-12)));   // small drift, under tolerance
        }

        public Task<bool> SetDeviceTimeAsync(DateTime serverTime) => Task.FromResult(true);

        public Task<List<DeviceUser>> GetUsersAsync()
        {
            var users = KnownUsers
                .Select((id, i) => new DeviceUser(id, $"Employee {id}", 0, true))
                .ToList();
            return Task.FromResult(users);
        }

        public Task<List<DevicePunch>> GetAttendanceLogsAsync(DateTime? since = null)
        {
            var punches = new List<DevicePunch>();
            var from = (since ?? DateTime.Today.AddDays(-3)).Date;
            var to = DateTime.Today;

            // Seed on the IP so each device produces its own consistent pattern.
            var ipSeed = _ip.GetHashCode();

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                // Saturday is the weekly off in Nepal - no punches.
                if (day.DayOfWeek == DayOfWeek.Saturday) continue;

                var rand = new Random(ipSeed ^ day.DayOfYear);

                foreach (var user in KnownUsers)
                {
                    // A few people are absent on any given day.
                    if (rand.Next(100) < 12) continue;

                    // Arrive somewhere between 08:50 and 09:25.
                    var inMinutes = 530 + rand.Next(0, 36);
                    var checkIn = day.AddMinutes(inMinutes).AddSeconds(rand.Next(60));
                    punches.Add(new DevicePunch(user, checkIn, 1, 0, 0));

                    // Most days include a lunch break.
                    if (rand.Next(100) < 70)
                    {
                        var breakOut = day.AddMinutes(780 + rand.Next(0, 20));
                        var breakIn = breakOut.AddMinutes(30 + rand.Next(0, 25));
                        punches.Add(new DevicePunch(user, breakOut, 1, 2, 0));
                        punches.Add(new DevicePunch(user, breakIn, 1, 3, 0));
                    }

                    // Leave between 17:00 and 17:50, unless they forgot to punch out.
                    if (rand.Next(100) < 92)
                    {
                        var outMinutes = 1020 + rand.Next(0, 50);
                        var checkOut = day.AddMinutes(outMinutes).AddSeconds(rand.Next(60));
                        punches.Add(new DevicePunch(user, checkOut, 1, 1, 0));
                    }
                }
            }

            _logger.LogInformation(
                "FakeDeviceReader produced {Count} punches for {Ip}", punches.Count, _ip);

            return Task.FromResult(punches.OrderBy(p => p.PunchTime).ToList());
        }

        public void Dispose() { }
    }
}
