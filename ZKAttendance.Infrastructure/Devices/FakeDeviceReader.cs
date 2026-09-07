using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Devices
{
    /// <summary>
    /// Produces believable punches, and now a believable USER TABLE, without any
    /// hardware.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// zkemkeeper.dll is a 32-bit COM component. It needs regsvr32, it forces
    /// the project to target x86, and it cannot run on a machine that has no
    /// device on the network. Without a fake, nothing about the sync pipeline
    /// can be developed, tested or demonstrated.
    ///
    /// WHAT CHANGED
    /// ------------
    /// The reader used to be read-only, so the enrolment flow — create the user
    /// on the terminal, ask it to capture a finger, pull the template back,
    /// push it to the other terminals — had nothing to run against. This class
    /// now keeps a per-IP in-memory user and template table, so the whole
    /// master/slave propagation path can be exercised end to end with
    /// DeviceProtocol = "Fake". Enrolment "succeeds" after a short delay, the
    /// way a person pressing a finger would.
    ///
    /// State is static and keyed by IP because a new reader instance is created
    /// per operation; a terminal, by contrast, remembers who is on it.
    ///
    /// Switch it on and off in appsettings.json:
    ///     "SyncConfiguration": { "DeviceProtocol": "Fake" }
    /// </summary>
    public class FakeDeviceReader : IZkDeviceReader
    {
        private readonly ILogger<FakeDeviceReader> _logger;
        private string _ip = "";
        private bool _connected;

        /// <summary>Biometric IDs every simulated device ships with.</summary>
        private static readonly string[] SeedUsers =
            { "1001", "1002", "1003", "1004", "1005", "1006", "1007", "1008" };

        private sealed class FakeDeviceState
        {
            public readonly Dictionary<string, DeviceUser> Users = new();
            public readonly List<FingerTemplate> Templates = new();
            /// <summary>Enrol number → when the simulated capture completes.</summary>
            public readonly Dictionary<string, DateTime> PendingEnrolments = new();
            public int NextUid = 1;
        }

        // One simulated terminal per IP, shared across reader instances.
        private static readonly ConcurrentDictionary<string, FakeDeviceState> Devices = new();

        public FakeDeviceReader(ILogger<FakeDeviceReader> logger) => _logger = logger;

        private FakeDeviceState State => Devices.GetOrAdd(_ip, _ =>
        {
            var state = new FakeDeviceState();
            foreach (var id in SeedUsers)
            {
                state.Users[id] = new DeviceUser(id, $"Employee {id}", 0, true)
                {
                    Uid = state.NextUid++,
                    FingerCount = 1
                };
                state.Templates.Add(new FingerTemplate(id, 0, FakeTemplateBytes(id, 0))
                {
                    Uid = state.Users[id].Uid
                });
            }
            return state;
        });

        /// <summary>
        /// A deterministic blob standing in for a real template, so propagation
        /// can be checked for "did the right bytes reach the right device"
        /// without a sensor.
        /// </summary>
        private static byte[] FakeTemplateBytes(string userId, int finger)
        {
            var seed = (userId + ":" + finger).GetHashCode();
            var rand = new Random(seed);
            var data = new byte[498]; // a real ZK v10 template is ~500 bytes
            rand.NextBytes(data);
            return data;
        }

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
                UserCount: State.Users.Count,
                LogCount: 0,
                DeviceTime: DateTime.Now.AddSeconds(-12)));   // small drift, under tolerance
        }

        public Task<bool> SetDeviceTimeAsync(DateTime serverTime) => Task.FromResult(true);

        public Task<List<DeviceUser>> GetUsersAsync()
        {
            CompleteDueEnrolments();
            return Task.FromResult(State.Users.Values.OrderBy(u => u.Uid).ToList());
        }

        public Task<List<DevicePunch>> GetAttendanceLogsAsync(DateTime? since = null)
        {
            var punches = new List<DevicePunch>();
            var from = (since ?? DateTime.Today.AddDays(-3)).Date;
            var to = DateTime.Today;

            // Only people actually on this device can punch on it.
            var users = State.Users.Keys.OrderBy(k => k).ToList();
            if (users.Count == 0) return Task.FromResult(punches);

            // Seed on the IP so each device produces its own consistent pattern.
            var ipSeed = _ip.GetHashCode();

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                // Saturday is the weekly off in Nepal - no punches.
                if (day.DayOfWeek == DayOfWeek.Saturday) continue;

                var rand = new Random(ipSeed ^ day.DayOfYear);

                foreach (var user in users)
                {
                    // A few people are absent on any given day.
                    if (rand.Next(100) < 12) continue;

                    // Arrive somewhere between 08:50 and 09:25.
                    var inMinutes = 530 + rand.Next(0, 36);
                    var checkIn = day.AddMinutes(inMinutes).AddSeconds(rand.Next(60));

                    // Never invent a punch that has not happened yet — the
                    // overview would then show tomorrow as a working day
                    // somebody attended.
                    if (checkIn > DateTime.Now) continue;
                    punches.Add(new DevicePunch(user, checkIn, 1, 0, 0));

                    // Most days include a lunch break.
                    if (rand.Next(100) < 70)
                    {
                        var breakOut = day.AddMinutes(780 + rand.Next(0, 20));
                        var breakIn = breakOut.AddMinutes(30 + rand.Next(0, 25));
                        if (breakOut <= DateTime.Now) punches.Add(new DevicePunch(user, breakOut, 1, 2, 0));
                        if (breakIn <= DateTime.Now) punches.Add(new DevicePunch(user, breakIn, 1, 3, 0));
                    }

                    // Leave between 17:00 and 17:50, unless they forgot to punch out.
                    if (rand.Next(100) < 92)
                    {
                        var outMinutes = 1020 + rand.Next(0, 50);
                        var checkOut = day.AddMinutes(outMinutes).AddSeconds(rand.Next(60));
                        if (checkOut <= DateTime.Now)
                            punches.Add(new DevicePunch(user, checkOut, 1, 1, 0));
                    }
                }
            }

            _logger.LogInformation(
                "FakeDeviceReader produced {Count} punches for {Ip}", punches.Count, _ip);

            return Task.FromResult(punches.OrderBy(p => p.PunchTime).ToList());
        }

        // ── write half ──────────────────────────────────────────────────

        public Task<bool> SetUserAsync(
            string biometricUserId,
            string name,
            int privilege = 0,
            string password = "",
            bool enabled = true)
        {
            var state = State;
            var uid = state.Users.TryGetValue(biometricUserId, out var existing)
                ? existing.Uid
                : state.NextUid++;

            state.Users[biometricUserId] = new DeviceUser(biometricUserId, name, privilege, enabled)
            {
                Uid = uid,
                FingerCount = state.Templates.Count(t => t.BiometricUserId == biometricUserId)
            };

            _logger.LogInformation(
                "FakeDeviceReader wrote user {Id} ('{Name}') to {Ip} in slot {Uid}",
                biometricUserId, name, _ip, uid);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteUserAsync(string biometricUserId)
        {
            var state = State;
            var removed = state.Users.Remove(biometricUserId);
            state.Templates.RemoveAll(t => t.BiometricUserId == biometricUserId);
            state.PendingEnrolments.Remove(biometricUserId);
            return Task.FromResult(removed);
        }

        /// <summary>
        /// Simulates the terminal switching to "place your finger". The capture
        /// completes a few seconds later, which is what makes the polling in
        /// DeviceEnrollmentOrchestrator exercisable without a person present.
        /// </summary>
        public Task<EnrollResult> StartRemoteEnrollAsync(string biometricUserId, int fingerIndex = 0)
        {
            var state = State;
            if (!state.Users.ContainsKey(biometricUserId))
            {
                return Task.FromResult(new EnrollResult(false,
                    "The user does not exist on this terminal yet — write the user record first.",
                    fingerIndex));
            }

            state.PendingEnrolments[biometricUserId] = DateTime.Now.AddSeconds(6);

            _logger.LogInformation(
                "FakeDeviceReader started enrolment on {Ip} for {Id}, finger {Finger}",
                _ip, biometricUserId, fingerIndex);

            return Task.FromResult(new EnrollResult(true,
                "Simulated terminal is waiting for the scan (completes in ~6s).", fingerIndex));
        }

        public Task<bool> CancelCaptureAsync()
        {
            State.PendingEnrolments.Clear();
            return Task.FromResult(true);
        }

        public Task<List<FingerTemplate>> GetTemplatesAsync(string? biometricUserId = null)
        {
            CompleteDueEnrolments();
            var list = State.Templates
                .Where(t => biometricUserId is null
                            || string.Equals(t.BiometricUserId, biometricUserId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(list);
        }

        public Task<bool> SetTemplateAsync(FingerTemplate template)
        {
            var state = State;
            if (!state.Users.TryGetValue(template.BiometricUserId, out var user))
            {
                _logger.LogWarning(
                    "FakeDeviceReader refused a template for {Id} on {Ip}: user not on the device",
                    template.BiometricUserId, _ip);
                return Task.FromResult(false);
            }

            state.Templates.RemoveAll(t =>
                t.BiometricUserId == template.BiometricUserId && t.FingerIndex == template.FingerIndex);

            state.Templates.Add(template with { Uid = user.Uid });

            _logger.LogInformation(
                "FakeDeviceReader stored template for {Id} finger {Finger} on {Ip}",
                template.BiometricUserId, template.FingerIndex, _ip);
            return Task.FromResult(true);
        }

        public Task<bool> RefreshDataAsync() => Task.FromResult(true);

        /// <summary>Turn any enrolment whose simulated capture window has elapsed into a template.</summary>
        private void CompleteDueEnrolments()
        {
            var state = State;
            if (state.PendingEnrolments.Count == 0) return;

            var due = state.PendingEnrolments
                .Where(kv => kv.Value <= DateTime.Now)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var userId in due)
            {
                state.PendingEnrolments.Remove(userId);
                if (!state.Users.TryGetValue(userId, out var user)) continue;

                var nextFinger = state.Templates.Count(t => t.BiometricUserId == userId);
                state.Templates.Add(new FingerTemplate(userId, nextFinger, FakeTemplateBytes(userId, nextFinger))
                {
                    Uid = user.Uid
                });
                state.Users[userId] = user with { };

                _logger.LogInformation(
                    "FakeDeviceReader completed a simulated enrolment for {Id} on {Ip}", userId, _ip);
            }
        }

        public void Dispose() { }
    }
}
