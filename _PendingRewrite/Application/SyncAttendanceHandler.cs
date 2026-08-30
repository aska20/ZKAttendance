using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Attendance;

public record DeviceAttendanceResult(
    int DeviceId, string DeviceName, bool Success,
    int Fetched, int Inserted, int Duplicates, int Unmapped, string? Error);

public interface ISyncAttendance
{
    Task<IReadOnlyList<DeviceAttendanceResult>> SyncAllAsync(CancellationToken ct = default);
    Task<DeviceAttendanceResult> SyncDeviceAsync(int deviceId, CancellationToken ct = default);
}

public class SyncAttendanceOptions
{
    public int MinimumPunchIntervalSeconds { get; set; } = 60;
    public int ClockDriftToleranceSeconds { get; set; } = 60;
    public int OverlapWindowDays { get; set; } = 2;
}

/// <summary>
/// Pulls punches from every device - master included, since the master records
/// attendance like any other machine - and resolves them to employees.
///
/// THE RESOLUTION STEP IS THE WHOLE POINT
/// --------------------------------------
/// A device sends only a number. That number means nothing on its own: 1017 on
/// the head office machine and 1017 on the branch machine may be two different
/// people. The lookup is therefore always scoped to the device it came from.
///
/// After resolution the device stops mattering. Attendance is grouped by
/// employee and date, which is what makes "check in at head office, check out
/// at the branch" produce one working day with no special-case code.
/// </summary>
public class SyncAttendanceHandler : ISyncAttendance
{
    private readonly IDeviceRepository _devices;
    private readonly IMappingRepository _mappings;
    private readonly IAttendanceLogRepository _logs;
    private readonly ISyncLogRepository _syncLogs;
    private readonly IZkDeviceClientFactory _clientFactory;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly INepaliCalendar _nepali;
    private readonly SyncAttendanceOptions _options;
    private readonly ILogger<SyncAttendanceHandler> _log;

    public SyncAttendanceHandler(
        IDeviceRepository devices,
        IMappingRepository mappings,
        IAttendanceLogRepository logs,
        ISyncLogRepository syncLogs,
        IZkDeviceClientFactory clientFactory,
        IUnitOfWork uow,
        IClock clock,
        INepaliCalendar nepali,
        SyncAttendanceOptions options,
        ILogger<SyncAttendanceHandler> log)
    {
        _devices = devices;
        _mappings = mappings;
        _logs = logs;
        _syncLogs = syncLogs;
        _clientFactory = clientFactory;
        _uow = uow;
        _clock = clock;
        _nepali = nepali;
        _options = options;
        _log = log;
    }

    public async Task<IReadOnlyList<DeviceAttendanceResult>> SyncAllAsync(CancellationToken ct = default)
    {
        var devices = await _devices.GetActiveAsync(ct);
        var results = new List<DeviceAttendanceResult>();

        // Sequential deliberately. The SDK is not thread-safe and the devices
        // usually share one LAN; four simultaneous sessions produce timeouts
        // that look exactly like hardware faults and waste hours of debugging.
        foreach (var device in devices)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await SyncDeviceAsync(device.DeviceId, ct));
        }

        _log.LogInformation(
            "Attendance round finished: {Ok}/{Total} devices, {New} new punches",
            results.Count(r => r.Success), results.Count, results.Sum(r => r.Inserted));

        return results;
    }

    public async Task<DeviceAttendanceResult> SyncDeviceAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _devices.GetAsync(deviceId, ct);
        if (device is null)
            return new DeviceAttendanceResult(deviceId, "?", false, 0, 0, 0, 0, "Device not found");

        var syncLog = new DeviceSyncLog
        {
            DeviceId = device.DeviceId,
            Operation = "Attendance",
            StartedUtc = _clock.UtcNow
        };

        await using var client = _clientFactory.Create();

        try
        {
            if (!await client.ConnectAsync(device.DeviceIp, device.DevicePort, ct))
                throw new InvalidOperationException(
                    $"Cannot reach {device.DeviceIp}:{device.DevicePort}");

            // Correct the clock BEFORE reading. A branch machine four minutes
            // slow makes one arrival look like two different times, and no
            // amount of later processing can undo that.
            var caps = await client.GetCapabilitiesAsync(ct);
            if (caps is not null)
            {
                var drift = (_clock.LocalNow - caps.DeviceTime).TotalSeconds;
                if (Math.Abs(drift) > _options.ClockDriftToleranceSeconds)
                {
                    _log.LogWarning("{Device} clock off by {Drift:F0}s - correcting",
                        device.DeviceName, drift);
                    await client.SetDeviceTimeAsync(_clock.LocalNow, ct);
                }
            }

            // Deliberate overlap. A device that was offline yesterday hands
            // over records we may already hold; the unique index sorts it out.
            var since = device.LastAttendancePullUtc?.AddDays(-_options.OverlapWindowDays);
            var punches = await client.GetPunchesAsync(since, ct);

            // Scoped to THIS device - the same number means different people
            // on different machines.
            var lookup = await _mappings.GetLookupForDeviceAsync(device.DeviceId, ct);

            var earliest = punches.Count > 0
                ? punches.Min(p => p.PunchTime).Date
                : _clock.Today;

            var existing = await _logs.GetExistingKeysAsync(device.DeviceId, earliest, ct);

            var minInterval = TimeSpan.FromSeconds(_options.MinimumPunchIntervalSeconds);
            var toInsert = new List<AttendanceLog>();
            var duplicates = 0;
            var unmapped = 0;

            foreach (var punch in punches.OrderBy(p => p.PunchTime))
            {
                if (existing.Contains((punch.DeviceUserId, punch.PunchTime)))
                {
                    duplicates++;
                    continue;
                }

                // Double tap, or a tap here followed by a tap at the next gate.
                if (existing.Any(e =>
                        e.DeviceUserId == punch.DeviceUserId &&
                        (punch.PunchTime - e.PunchTime).Duration() < minInterval))
                {
                    duplicates++;
                    continue;
                }

                int? employeeId = null;
                if (lookup.TryGetValue(punch.DeviceUserId, out var id))
                    employeeId = id;
                else
                    unmapped++;

                toInsert.Add(new AttendanceLog
                {
                    DeviceId = device.DeviceId,
                    BranchId = device.BranchId,
                    DeviceUserId = punch.DeviceUserId,
                    EmployeeId = employeeId,           // null is allowed and kept
                    PunchTimeAd = punch.PunchTime,     // stored in AD, untouched
                    PunchDateBs = _nepali.ToBsString(punch.PunchTime),
                    VerifyMode = punch.VerifyMode,
                    Direction = punch.Direction,
                    WorkCode = punch.WorkCode,
                    SourceType = "Device",
                    CreatedUtc = _clock.UtcNow
                });

                existing.Add((punch.DeviceUserId, punch.PunchTime));
            }

            var inserted = toInsert.Count;

            try
            {
                await _logs.AddRangeAsync(toInsert, ct);
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (_uow.IsUniqueViolation(ex))
            {
                // The index is the final authority and it just did its job.
                // Not an error - re-running a sync is supposed to be harmless.
                _log.LogInformation("Unique index rejected repeats from {Device}", device.DeviceName);
                duplicates += inserted;
                inserted = 0;
            }

            device.LastAttendancePullUtc = _clock.UtcNow;
            device.IsOnline = true;
            device.LastConnectionUtc = _clock.UtcNow;

            syncLog.Success = true;
            syncLog.RecordsFetched = punches.Count;
            syncLog.RecordsInserted = inserted;
            syncLog.Duplicates = duplicates;
            syncLog.Unmapped = unmapped;
            syncLog.FinishedUtc = _clock.UtcNow;

            await _syncLogs.AddAsync(syncLog, ct);
            await _uow.SaveChangesAsync(ct);

            if (unmapped > 0)
                _log.LogWarning(
                    "{Count} punches from {Device} have no employee mapping. " +
                    "They are stored, not discarded - approve the enrolment to resolve them.",
                    unmapped, device.DeviceName);

            return new DeviceAttendanceResult(
                device.DeviceId, device.DeviceName, true,
                punches.Count, inserted, duplicates, unmapped, null);
        }
        catch (Exception ex)
        {
            // One device down must never stop the round.
            _log.LogError(ex, "Attendance sync failed for {Device}", device.DeviceName);

            device.IsOnline = false;
            syncLog.Success = false;
            syncLog.ErrorMessage = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            syncLog.FinishedUtc = _clock.UtcNow;

            await _syncLogs.AddAsync(syncLog, ct);
            await _uow.SaveChangesAsync(ct);

            return new DeviceAttendanceResult(
                device.DeviceId, device.DeviceName, false, 0, 0, 0, 0, ex.Message);
        }
    }
}
