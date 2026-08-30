using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Enums;

namespace ZKAttendance.Infrastructure.Devices;

/// <summary>
/// A simulated fleet of ZKTeco machines, keyed by IP.
///
/// State is deliberately static and shared, so a template pushed to
/// 192.168.5.14 in one request is still there in the next. That is what makes
/// the full flow demonstrable: enrol on the master, pull, approve, watch the
/// propagation job write to two slaves, then see punches for that person start
/// arriving from all three.
/// </summary>
public class FakeDeviceFleet
{
    private static readonly ConcurrentDictionary<string, FakeDeviceState> Devices = new();

    public static FakeDeviceState ForIp(string ip) =>
        Devices.GetOrAdd(ip, _ => new FakeDeviceState(ip));

    /// <summary>
    /// Simulates HR enrolling somebody at the master machine. Called from the
    /// demo controller so the pending-enrolment queue can be shown filling up.
    /// </summary>
    public static void SimulateEnrollment(string masterIp, string deviceUserId, string name, int fingers = 2)
    {
        var device = ForIp(masterIp);
        device.Users[deviceUserId] = new DeviceUserRecord(deviceUserId, name, 0, true, null, null);

        var templates = new List<DeviceTemplateRecord>();
        for (var finger = 0; finger < fingers; finger++)
        {
            // Deterministic pseudo-template. Real ones are ~600 opaque bytes
            // produced by the sensor's own algorithm.
            var data = new byte[512];
            var seed = new Random(deviceUserId.GetHashCode() ^ finger);
            seed.NextBytes(data);
            templates.Add(new DeviceTemplateRecord(deviceUserId, finger, data, 10));
        }

        device.Templates[deviceUserId] = templates;
    }

    public static void Reset() => Devices.Clear();
}

public class FakeDeviceState
{
    public FakeDeviceState(string ip) => Ip = ip;

    public string Ip { get; }
    public ConcurrentDictionary<string, DeviceUserRecord> Users { get; } = new();
    public ConcurrentDictionary<string, List<DeviceTemplateRecord>> Templates { get; } = new();
    public bool Enabled { get; set; } = true;

    /// <summary>Every device runs slightly off. 40 seconds is under tolerance.</summary>
    public TimeSpan ClockOffset { get; set; } = TimeSpan.FromSeconds(-40);
}

/// <summary>
/// Adapter that talks to the simulated fleet instead of real hardware.
///
/// Punches are only generated for users that actually exist on that device,
/// which is the behaviour that matters: an employee who has not been
/// propagated to a slave yet produces no punches there, exactly as a real
/// machine would refuse to recognise them.
/// </summary>
public class FakeZkDeviceClient : IZkDeviceClient
{
    private readonly ILogger<FakeZkDeviceClient> _log;
    private FakeDeviceState? _state;

    public FakeZkDeviceClient(ILogger<FakeZkDeviceClient> log) => _log = log;

    public bool IsConnected => _state is not null;

    public Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default)
    {
        _state = FakeDeviceFleet.ForIp(ip);
        _log.LogDebug("Fake client connected to {Ip}:{Port}", ip, port);
        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        _state = null;
        return Task.CompletedTask;
    }

    public Task<DeviceCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        if (_state is null) return Task.FromResult<DeviceCapabilities?>(null);

        return Task.FromResult<DeviceCapabilities?>(new DeviceCapabilities(
            SerialNumber: "SIM-" + _state.Ip.Replace(".", ""),
            FirmwareVersion: "Ver 6.60 (simulated)",
            TemplateFormatVersion: 10,
            UserCapacity: 3000,
            UserCount: _state.Users.Count,
            LogCount: 0,
            DeviceTime: DateTime.Now.Add(_state.ClockOffset)));
    }

    public Task<bool> SetDeviceTimeAsync(DateTime serverTime, CancellationToken ct = default)
    {
        if (_state is not null) _state.ClockOffset = TimeSpan.Zero;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<DeviceUserRecord>> GetUsersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DeviceUserRecord>>(
            _state?.Users.Values.ToList() ?? new List<DeviceUserRecord>());

    public Task<IReadOnlyList<DeviceTemplateRecord>> GetTemplatesAsync(
        string deviceUserId, CancellationToken ct = default)
    {
        var found = _state is not null && _state.Templates.TryGetValue(deviceUserId, out var list)
            ? list
            : new List<DeviceTemplateRecord>();

        return Task.FromResult<IReadOnlyList<DeviceTemplateRecord>>(found);
    }

    public Task<IReadOnlyList<DevicePunchRecord>> GetPunchesAsync(
        DateTime? since = null, CancellationToken ct = default)
    {
        var punches = new List<DevicePunchRecord>();
        if (_state is null)
            return Task.FromResult<IReadOnlyList<DevicePunchRecord>>(punches);

        var from = (since ?? DateTime.Today.AddDays(-3)).Date;
        var to = DateTime.Today;
        var ipSeed = _state.Ip.GetHashCode();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            // Saturday is the weekly off in Nepal.
            if (day.DayOfWeek == DayOfWeek.Saturday) continue;

            var rand = new Random(ipSeed ^ day.DayOfYear);

            // Only people actually present on this device can punch here.
            foreach (var userId in _state.Users.Keys.OrderBy(k => k))
            {
                if (rand.Next(100) < 12) continue;   // absent

                var inAt = day.AddMinutes(530 + rand.Next(0, 36)).AddSeconds(rand.Next(60));
                punches.Add(new DevicePunchRecord(
                    userId, inAt, VerifyMode.Fingerprint, PunchDirection.CheckIn, 0));

                if (rand.Next(100) < 70)
                {
                    var breakOut = day.AddMinutes(780 + rand.Next(0, 20));
                    punches.Add(new DevicePunchRecord(
                        userId, breakOut, VerifyMode.Fingerprint, PunchDirection.BreakOut, 0));
                    punches.Add(new DevicePunchRecord(
                        userId, breakOut.AddMinutes(30 + rand.Next(0, 25)),
                        VerifyMode.Fingerprint, PunchDirection.BreakIn, 0));
                }

                if (rand.Next(100) < 92)
                    punches.Add(new DevicePunchRecord(
                        userId, day.AddMinutes(1020 + rand.Next(0, 50)).AddSeconds(rand.Next(60)),
                        VerifyMode.Fingerprint, PunchDirection.CheckOut, 0));
            }
        }

        return Task.FromResult<IReadOnlyList<DevicePunchRecord>>(
            punches.OrderBy(p => p.PunchTime).ToList());
    }

    public Task<bool> UpsertUserAsync(DeviceUserRecord user, CancellationToken ct = default)
    {
        if (_state is null) return Task.FromResult(false);
        _state.Users[user.DeviceUserId] = user;
        return Task.FromResult(true);
    }

    public Task<bool> WriteTemplateAsync(DeviceTemplateRecord template, CancellationToken ct = default)
    {
        if (_state is null) return Task.FromResult(false);

        // Mirrors real firmware: a template for an unknown enrol number is
        // rejected. This is what catches "user record written after template".
        if (!_state.Users.ContainsKey(template.DeviceUserId))
        {
            _log.LogWarning(
                "Template rejected for unknown user {Id} on {Ip} - write the user record first",
                template.DeviceUserId, _state.Ip);
            return Task.FromResult(false);
        }

        var list = _state.Templates.GetOrAdd(template.DeviceUserId, _ => new List<DeviceTemplateRecord>());
        lock (list)
        {
            list.RemoveAll(t => t.FingerIndex == template.FingerIndex);
            list.Add(template);
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeleteUserAsync(string deviceUserId, CancellationToken ct = default)
    {
        if (_state is null) return Task.FromResult(false);
        _state.Users.TryRemove(deviceUserId, out _);
        _state.Templates.TryRemove(deviceUserId, out _);
        return Task.FromResult(true);
    }

    public Task RefreshDataAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> DisableAsync(int timeoutSeconds = 30, CancellationToken ct = default)
    {
        if (_state is not null) _state.Enabled = false;
        return Task.FromResult(true);
    }

    public Task<bool> EnableAsync(CancellationToken ct = default)
    {
        if (_state is not null) _state.Enabled = true;
        return Task.FromResult(true);
    }

    public ValueTask DisposeAsync()
    {
        _state = null;
        return ValueTask.CompletedTask;
    }
}

public class FakeZkDeviceClientFactory : IZkDeviceClientFactory
{
    private readonly ILoggerFactory _loggerFactory;
    public FakeZkDeviceClientFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public IZkDeviceClient Create() =>
        new FakeZkDeviceClient(_loggerFactory.CreateLogger<FakeZkDeviceClient>());
}
