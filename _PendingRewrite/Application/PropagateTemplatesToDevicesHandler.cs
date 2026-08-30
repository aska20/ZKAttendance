using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Provisioning;

public record PropagationResult(
    int MappingsAttempted,
    int Succeeded,
    int Failed,
    int DevicesUnreachable,
    IReadOnlyList<string> Errors);

public interface IPropagateTemplatesToDevices
{
    /// <summary>Push every mapping still waiting for its template.</summary>
    Task<PropagationResult> ExecutePendingAsync(CancellationToken ct = default);

    /// <summary>Fill a newly added device with every active employee.</summary>
    Task<PropagationResult> ProvisionDeviceAsync(int deviceId, CancellationToken ct = default);
}

/// <summary>
/// Carries fingerprint templates from the database out to the slave devices.
///
/// THIS IS THE "DEVICES TALKING TO EACH OTHER" PART
/// ------------------------------------------------
/// They do not, and cannot. A ZKTeco machine has no concept of a peer and no
/// way to address one. What actually happens is:
///
///     master --pull--> application --push--> slave 1
///                                   --push--> slave 2
///                                   --push--> slave 3
///
/// The application is the only component holding every device's IP, so it is
/// the only thing that can bridge them. From the operator's point of view the
/// fingerprint "moves" from the main device to the others; mechanically it
/// travels through the database, which is also what makes it survive the
/// master being replaced.
///
/// ORDER OF OPERATIONS ON EACH DEVICE
/// ----------------------------------
/// Disable -> upsert user -> write templates -> refresh -> enable.
///
/// The user record must exist before its template is written; most firmware
/// silently discards a template for an unknown enrol number, which produces
/// the classic "I pushed them but the branch machine does not know them".
/// Disabling first stops somebody punching mid-write and getting a partial
/// match. Enable runs in a finally block - leaving a device disabled would
/// lock a whole branch out of the building.
/// </summary>
public class PropagateTemplatesToDevicesHandler : IPropagateTemplatesToDevices
{
    private readonly IDeviceRepository _devices;
    private readonly IEmployeeRepository _employees;
    private readonly IMappingRepository _mappings;
    private readonly ITemplateRepository _templates;
    private readonly ISyncLogRepository _syncLogs;
    private readonly IZkDeviceClientFactory _clientFactory;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<PropagateTemplatesToDevicesHandler> _log;

    public PropagateTemplatesToDevicesHandler(
        IDeviceRepository devices,
        IEmployeeRepository employees,
        IMappingRepository mappings,
        ITemplateRepository templates,
        ISyncLogRepository syncLogs,
        IZkDeviceClientFactory clientFactory,
        IUnitOfWork uow,
        IClock clock,
        ILogger<PropagateTemplatesToDevicesHandler> log)
    {
        _devices = devices;
        _employees = employees;
        _mappings = mappings;
        _templates = templates;
        _syncLogs = syncLogs;
        _clientFactory = clientFactory;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    public async Task<PropagationResult> ExecutePendingAsync(CancellationToken ct = default)
    {
        var pending = await _mappings.GetPendingTemplateSyncAsync(ct);

        if (pending.Count == 0)
            return new PropagationResult(0, 0, 0, 0, Array.Empty<string>());

        // Grouped by device so each machine is opened once, not once per
        // employee. Twelve new joiners on three devices is 3 connections,
        // not 36.
        var byDevice = pending.GroupBy(m => m.DeviceId);

        var succeeded = 0;
        var failed = 0;
        var unreachable = 0;
        var errors = new List<string>();

        foreach (var group in byDevice)
        {
            ct.ThrowIfCancellationRequested();

            var device = await _devices.GetAsync(group.Key, ct);
            if (device is null || !device.IsActive) continue;

            var result = await PushToDeviceAsync(device, group.ToList(), ct);

            succeeded += result.Succeeded;
            failed += result.Failed;
            unreachable += result.DevicesUnreachable;
            errors.AddRange(result.Errors);
        }

        return new PropagationResult(pending.Count, succeeded, failed, unreachable, errors);
    }

    /// <summary>
    /// Fills a brand-new device with everyone.
    ///
    /// This is what makes adding a fourth machine a five-minute background job
    /// rather than calling three hundred people back to press their finger
    /// again. It works only because the templates were cached at enrolment.
    /// </summary>
    public async Task<PropagationResult> ProvisionDeviceAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _devices.GetAsync(deviceId, ct);
        if (device is null)
            return new PropagationResult(0, 0, 0, 0, new[] { "Device not found" });

        _log.LogInformation("Provisioning {Device} with all active employees", device.DeviceName);

        var employees = await _employees.GetActiveAsync(ct);
        var used = await _mappings.GetUsedDeviceUserIdsAsync(deviceId, ct);
        var toPush = new List<EmployeeDeviceMapping>();

        foreach (var employee in employees)
        {
            if (!await _templates.HasTemplatesAsync(employee.EmployeeId, ct))
            {
                // Enrolled before template caching existed, or enrolled by
                // password only. They must be re-enrolled on the master; there
                // is nothing to copy.
                _log.LogWarning(
                    "Employee {Id} ({Name}) has no cached template and cannot be " +
                    "provisioned - re-enrol on the master device",
                    employee.EmployeeId, employee.FullName);
                continue;
            }

            var existing = await _mappings.FindAsync(employee.EmployeeId, deviceId, ct);

            if (existing is not null)
            {
                if (existing.TemplateSyncedUtc is null) toPush.Add(existing);
                continue;
            }

            var deviceUserId = Enrollment.ApprovePendingEnrollmentHandler
                .AllocateDeviceUserId(employee.MasterDeviceUserId, used);

            used.Add(deviceUserId);

            var mapping = new EmployeeDeviceMapping
            {
                EmployeeId = employee.EmployeeId,
                DeviceId = deviceId,
                DeviceUserId = deviceUserId,
                IsEnrolled = false,
                IsActive = true,
                CreatedUtc = _clock.UtcNow
            };

            await _mappings.AddAsync(mapping, ct);
            toPush.Add(mapping);
        }

        await _uow.SaveChangesAsync(ct);

        var result = await PushToDeviceAsync(device, toPush, ct);

        // Only call it provisioned if nothing failed. A half-filled device that
        // reports itself ready is worse than one that admits it is not.
        if (result.Failed == 0 && result.DevicesUnreachable == 0)
        {
            device.IsProvisioned = true;
            await _uow.SaveChangesAsync(ct);
            _log.LogInformation(
                "{Device} provisioned: {Count} employees written", device.DeviceName, result.Succeeded);
        }

        return result;
    }

    private async Task<PropagationResult> PushToDeviceAsync(
        Device device, IReadOnlyList<EmployeeDeviceMapping> mappings, CancellationToken ct)
    {
        if (mappings.Count == 0)
            return new PropagationResult(0, 0, 0, 0, Array.Empty<string>());

        var syncLog = new DeviceSyncLog
        {
            DeviceId = device.DeviceId,
            Operation = "Provisioning",
            StartedUtc = _clock.UtcNow
        };

        var succeeded = 0;
        var failed = 0;
        var errors = new List<string>();

        await using var client = _clientFactory.Create();

        try
        {
            if (!await client.ConnectAsync(device.DeviceIp, device.DevicePort, ct))
                throw new InvalidOperationException(
                    $"Cannot reach {device.DeviceName} at {device.DeviceIp}:{device.DevicePort}");

            var caps = await client.GetCapabilitiesAsync(ct);

            // Take the device out of service for the duration of the write.
            await client.DisableAsync(60, ct);

            try
            {
                foreach (var mapping in mappings)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        await PushOneAsync(client, device, mapping, caps, ct);
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        // One bad employee must not abandon the other 299.
                        failed++;
                        mapping.LastSyncError = Truncate(ex.Message, 400);
                        errors.Add($"{device.DeviceName}/{mapping.DeviceUserId}: {ex.Message}");

                        _log.LogError(ex,
                            "Failed pushing employee {EmployeeId} to {Device}",
                            mapping.EmployeeId, device.DeviceName);
                    }
                }

                await client.RefreshDataAsync(ct);
            }
            finally
            {
                // Always re-enable, even if the loop threw. A device left
                // disabled locks the branch out.
                await client.EnableAsync(ct);
            }

            device.IsOnline = true;
            device.LastConnectionUtc = _clock.UtcNow;

            syncLog.Success = failed == 0;
            syncLog.RecordsFetched = mappings.Count;
            syncLog.RecordsInserted = succeeded;
            syncLog.FinishedUtc = _clock.UtcNow;
            if (failed > 0) syncLog.ErrorMessage = $"{failed} of {mappings.Count} failed";

            await _syncLogs.AddAsync(syncLog, ct);
            await _uow.SaveChangesAsync(ct);

            return new PropagationResult(mappings.Count, succeeded, failed, 0, errors);
        }
        catch (Exception ex)
        {
            // The device itself is unreachable. Nothing is marked as synced, so
            // the next run picks up exactly the same work.
            _log.LogError(ex, "Device {Device} unreachable during propagation", device.DeviceName);

            device.IsOnline = false;
            syncLog.Success = false;
            syncLog.ErrorMessage = Truncate(ex.Message, 500);
            syncLog.FinishedUtc = _clock.UtcNow;

            await _syncLogs.AddAsync(syncLog, ct);
            await _uow.SaveChangesAsync(ct);

            return new PropagationResult(mappings.Count, 0, 0, 1,
                new[] { $"{device.DeviceName}: {ex.Message}" });
        }
    }

    private async Task PushOneAsync(
        IZkDeviceClient client, Device device,
        EmployeeDeviceMapping mapping, DeviceCapabilities? caps, CancellationToken ct)
    {
        var employee = await _employees.GetAsync(mapping.EmployeeId, ct)
            ?? throw new InvalidOperationException($"Employee {mapping.EmployeeId} no longer exists");

        var templates = await _templates.GetForEmployeeAsync(mapping.EmployeeId, ct);

        if (templates.Count == 0)
            throw new InvalidOperationException("No cached fingerprint template");

        // Template formats are not interchangeable between firmware families.
        // Writing a v10 template to a v9 device produces a user who exists but
        // whose finger never matches - a failure that is invisible until
        // somebody stands at the door and it does not open.
        if (caps is not null && templates[0].TemplateFormatVersion != caps.TemplateFormatVersion)
            throw new InvalidOperationException(
                $"Template format v{templates[0].TemplateFormatVersion} cannot be written to " +
                $"{device.DeviceName}, which expects v{caps.TemplateFormatVersion}. " +
                "Both devices must run the same firmware family.");

        // 1. The user record first - the template has nothing to attach to otherwise.
        var ok = await client.UpsertUserAsync(new DeviceUserRecord(
            DeviceUserId: mapping.DeviceUserId,
            Name: Shorten(employee.FullName, 24),   // device name fields are short
            Privilege: 0,
            Enabled: true,
            Password: null,
            CardNumber: null), ct);

        if (!ok) throw new InvalidOperationException("Device rejected the user record");

        // 2. Then every finger. Pushing only one means the other fingers stop
        //    working at this door but keep working at the master, which looks
        //    like a faulty sensor.
        foreach (var template in templates)
        {
            var written = await client.WriteTemplateAsync(new DeviceTemplateRecord(
                mapping.DeviceUserId,
                template.FingerIndex,
                template.TemplateData,
                template.TemplateFormatVersion), ct);

            if (!written)
                throw new InvalidOperationException(
                    $"Device rejected the template for finger {template.FingerIndex}");
        }

        mapping.IsEnrolled = true;
        mapping.TemplateSyncedUtc = _clock.UtcNow;
        mapping.LastSyncError = null;
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max];
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
