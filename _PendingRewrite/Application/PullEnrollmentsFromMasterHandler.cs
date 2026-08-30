using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Enrollment;

public record PullEnrollmentsResult(
    bool Success,
    int UsersOnDevice,
    int NewDiscovered,
    int AlreadyKnown,
    int TemplatesRefreshed,
    string? Error);

public interface IPullEnrollmentsFromMaster
{
    Task<PullEnrollmentsResult> ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads the master device and discovers people who were enrolled on it.
///
/// THE FLOW THIS IMPLEMENTS
/// ------------------------
/// 1. HR walks a new joiner to the master machine and enrols their finger
///    there. That is the only place a fingerprint can physically be captured -
///    a web form cannot read a thumb.
/// 2. This job runs on a timer, connects to the master, and lists its users.
/// 3. Any enrol number the database has never seen becomes a PendingEnrollment
///    row, together with the templates captured at the same moment.
/// 4. HR opens the portal, sees "3 new enrolments awaiting review", fills in
///    name, department and shift, and approves. Only then does an Employee
///    exist.
///
/// WHY NOT CREATE THE EMPLOYEE AUTOMATICALLY
/// -----------------------------------------
/// A technician testing the sensor, or a visitor enrolled for a day, would
/// silently become a payroll record. The device knows a number and sometimes a
/// half-typed name; it does not know a department, a shift or a salary. A human
/// has to supply those, so the row waits.
///
/// WHY TEMPLATES ARE PULLED HERE AND NOT AT APPROVAL TIME
/// ------------------------------------------------------
/// The master is reachable right now. By approval time it may be offline, or
/// its memory may have been cleared. Capturing the template at discovery means
/// approval never depends on hardware being available.
/// </summary>
public class PullEnrollmentsFromMasterHandler : IPullEnrollmentsFromMaster
{
    private readonly IDeviceRepository _devices;
    private readonly IPendingEnrollmentRepository _pending;
    private readonly IMappingRepository _mappings;
    private readonly ITemplateRepository _templates;
    private readonly ISyncLogRepository _syncLogs;
    private readonly IZkDeviceClientFactory _clientFactory;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<PullEnrollmentsFromMasterHandler> _log;

    public PullEnrollmentsFromMasterHandler(
        IDeviceRepository devices,
        IPendingEnrollmentRepository pending,
        IMappingRepository mappings,
        ITemplateRepository templates,
        ISyncLogRepository syncLogs,
        IZkDeviceClientFactory clientFactory,
        IUnitOfWork uow,
        IClock clock,
        ILogger<PullEnrollmentsFromMasterHandler> log)
    {
        _devices = devices;
        _pending = pending;
        _mappings = mappings;
        _templates = templates;
        _syncLogs = syncLogs;
        _clientFactory = clientFactory;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    public async Task<PullEnrollmentsResult> ExecuteAsync(CancellationToken ct = default)
    {
        var master = await _devices.GetMasterAsync(ct);

        if (master is null)
        {
            // Deliberately not falling back to "the first device". Picking the
            // wrong machine as the enrolment source would pull its local users
            // in as if they were new joiners.
            _log.LogWarning(
                "No master device is configured. Set one device's Role to Master " +
                "before enrolment can be pulled.");
            return new PullEnrollmentsResult(false, 0, 0, 0, 0, "No master device configured");
        }

        var syncLog = new DeviceSyncLog
        {
            DeviceId = master.DeviceId,
            Operation = "Enrollment",
            StartedUtc = _clock.UtcNow
        };

        await using var client = _clientFactory.Create();

        try
        {
            if (!await client.ConnectAsync(master.DeviceIp, master.DevicePort, ct))
                throw new InvalidOperationException(
                    $"Cannot reach master device at {master.DeviceIp}:{master.DevicePort}");

            var users = await client.GetUsersAsync(ct);

            // Everything this device has ever shown us - pending, approved or
            // rejected - plus everyone already mapped to it.
            var known = await _pending.GetKnownDeviceUserIdsAsync(master.DeviceId, ct);
            var mapped = await _mappings.GetUsedDeviceUserIdsAsync(master.DeviceId, ct);
            known.UnionWith(mapped);

            var discovered = 0;
            var refreshed = 0;

            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();

                if (known.Contains(user.DeviceUserId))
                {
                    // Already an employee. Re-pull the templates if they were
                    // re-enrolled on the device - a worn fingerprint being
                    // captured again is a normal event, and the fresh template
                    // needs to reach the slaves too.
                    refreshed += await RefreshTemplatesIfChangedAsync(client, master, user.DeviceUserId, ct);
                    continue;
                }

                var templates = await client.GetTemplatesAsync(user.DeviceUserId, ct);

                if (templates.Count == 0)
                {
                    // A user record with no finger. Usually someone half-way
                    // through enrolling. Skipping means it is picked up on the
                    // next pull once the finger is actually registered.
                    _log.LogInformation(
                        "Master user {Id} has no fingerprint template yet - skipping this round",
                        user.DeviceUserId);
                    continue;
                }

                await _pending.AddAsync(new PendingEnrollment
                {
                    DeviceId = master.DeviceId,
                    DeviceUserId = user.DeviceUserId,
                    NameOnDevice = string.IsNullOrWhiteSpace(user.Name) ? null : user.Name.Trim(),
                    FingerCount = templates.Count,
                    Privilege = user.Privilege,
                    DiscoveredUtc = _clock.UtcNow
                }, ct);

                // Park the templates against the pending row's device user id.
                // They are re-keyed to the EmployeeId at approval.
                await StashTemplatesAsync(master.DeviceId, user.DeviceUserId, templates, ct);

                discovered++;

                _log.LogInformation(
                    "New enrolment discovered on master: {Id} ({Name}), {Fingers} finger(s)",
                    user.DeviceUserId, user.Name, templates.Count);
            }

            master.LastEnrollmentPullUtc = _clock.UtcNow;
            master.IsOnline = true;

            syncLog.Success = true;
            syncLog.RecordsFetched = users.Count;
            syncLog.RecordsInserted = discovered;
            syncLog.FinishedUtc = _clock.UtcNow;
            await _syncLogs.AddAsync(syncLog, ct);

            await _uow.SaveChangesAsync(ct);

            return new PullEnrollmentsResult(
                true, users.Count, discovered, users.Count - discovered, refreshed, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Enrolment pull from master device failed");

            master.IsOnline = false;
            syncLog.Success = false;
            syncLog.ErrorMessage = Truncate(ex.Message, 500);
            syncLog.FinishedUtc = _clock.UtcNow;

            await _syncLogs.AddAsync(syncLog, ct);
            await _uow.SaveChangesAsync(ct);

            return new PullEnrollmentsResult(false, 0, 0, 0, 0, ex.Message);
        }
    }

    /// <summary>
    /// Templates for a not-yet-approved person are stored against EmployeeId 0
    /// and the device user id, then moved onto the real EmployeeId when the
    /// enrolment is approved.
    /// </summary>
    private Task StashTemplatesAsync(
        int deviceId, string deviceUserId,
        IReadOnlyList<DeviceTemplateRecord> templates, CancellationToken ct)
    {
        var staged = templates.Select(t => new FingerprintTemplate
        {
            EmployeeId = 0,
            FingerIndex = t.FingerIndex,
            TemplateData = t.TemplateData,
            TemplateFormatVersion = t.FormatVersion,
            SourceDeviceId = deviceId,
            CapturedUtc = _clock.UtcNow
        });

        return PendingTemplateStore.StageAsync(deviceId, deviceUserId, staged, ct);
    }

    private async Task<int> RefreshTemplatesIfChangedAsync(
        IZkDeviceClient client, Device master, string deviceUserId, CancellationToken ct)
    {
        var mapping = (await _mappings.GetLookupForDeviceAsync(master.DeviceId, ct))
            .TryGetValue(deviceUserId, out var employeeId) ? employeeId : (int?)null;

        if (mapping is null) return 0;

        var onDevice = await client.GetTemplatesAsync(deviceUserId, ct);
        if (onDevice.Count == 0) return 0;

        var stored = await _templates.GetForEmployeeAsync(mapping.Value, ct);

        // Compare finger count and bytes. Cheap, and avoids rewriting every
        // template on every pull, which would churn the slaves needlessly.
        var unchanged = stored.Count == onDevice.Count &&
                        onDevice.All(d => stored.Any(s =>
                            s.FingerIndex == d.FingerIndex &&
                            s.TemplateData.AsSpan().SequenceEqual(d.TemplateData)));

        if (unchanged) return 0;

        await _templates.ReplaceForEmployeeAsync(mapping.Value, onDevice.Select(d => new FingerprintTemplate
        {
            EmployeeId = mapping.Value,
            FingerIndex = d.FingerIndex,
            TemplateData = d.TemplateData,
            TemplateFormatVersion = d.FormatVersion,
            SourceDeviceId = master.DeviceId,
            CapturedUtc = _clock.UtcNow
        }), ct);

        // Force a re-push to every slave: the old template is now stale there.
        foreach (var m in await _mappings.GetForEmployeeAsync(mapping.Value, ct))
        {
            if (m.DeviceId == master.DeviceId) continue;
            m.TemplateSyncedUtc = null;
            m.IsEnrolled = false;
        }

        _log.LogInformation(
            "Template for employee {EmployeeId} changed on master - queued for re-push to slaves",
            mapping.Value);

        return 1;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>
/// Holding area for templates belonging to enrolments that have not been
/// approved yet. Implemented in Infrastructure against a staging table.
/// </summary>
public static class PendingTemplateStore
{
    public static Func<int, string, IEnumerable<FingerprintTemplate>, CancellationToken, Task> StageAsync { get; set; }
        = (_, _, _, _) => Task.CompletedTask;

    public static Func<int, string, CancellationToken, Task<IReadOnlyList<FingerprintTemplate>>> TakeAsync { get; set; }
        = (_, _, _) => Task.FromResult<IReadOnlyList<FingerprintTemplate>>(Array.Empty<FingerprintTemplate>());
}
