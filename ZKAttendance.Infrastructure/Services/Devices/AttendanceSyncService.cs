using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Enums;

namespace ZKAttendance.Infrastructure.Services.Devices
{
    public record DeviceSyncResult(
        int DeviceId,
        string DeviceName,
        bool Success,
        int Fetched,
        int Inserted,
        int Duplicates,
        int Unmapped,
        string? Error);

    public interface IAttendanceSyncService
    {
        Task<List<DeviceSyncResult>> SyncAllDevicesAsync(CancellationToken ct = default);
        Task<DeviceSyncResult> SyncDeviceAsync(int deviceId, CancellationToken ct = default);
    }

    /// <summary>
    /// Collects punches from every registered device and writes them into
    /// AttendanceLogs.
    ///
    /// THIS IS THE PIECE THE PROJECT DOES NOT HAVE YET.
    /// Today DeviceMonitorService only pings the device IP and checks whether
    /// port 4370 answers. Nothing reads the attendance log. This class is what
    /// turns "the device is reachable" into "the punches are in the database".
    ///
    /// DESIGN RULES
    /// ------------
    /// 1. One device failing must not stop the others. Each device is wrapped
    ///    in its own try/catch and its own SyncLog row.
    /// 2. Never trust the device to send only new records. Let the unique index
    ///    reject duplicates.
    /// 3. A punch whose biometric ID is not mapped to any employee is still
    ///    saved, with EmployeeId = null. Losing a punch because HR has not
    ///    finished the paperwork would be much worse than storing an orphan.
    /// 4. Store the AD timestamp untouched. BS conversion happens at display
    ///    time, never on the way in.
    /// </summary>
    public class AttendanceSyncService : IAttendanceSyncService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Func<IZkDeviceReader> _readerFactory;
        private readonly ILogger<AttendanceSyncService> _logger;

        /// <summary>
        /// Two punches by the same person closer together than this are treated
        /// as one event. Stops a double-tap, or a tap at one gate followed by a
        /// tap at the next gate, from looking like two separate movements.
        /// </summary>
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(60);

        public AttendanceSyncService(
            IServiceScopeFactory scopeFactory,
            Func<IZkDeviceReader> readerFactory,
            ILogger<AttendanceSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _readerFactory = readerFactory;
            _logger = logger;
        }

        public async Task<List<DeviceSyncResult>> SyncAllDevicesAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            var deviceIds = await db.Devices
                .Where(d => d.IsActive)
                .Select(d => d.DeviceId)
                .ToListAsync(ct);

            _logger.LogInformation("Starting sync round for {Count} device(s)", deviceIds.Count);

            var results = new List<DeviceSyncResult>();

            // Sequential on purpose. Devices are usually on the same LAN and the
            // SDK is not thread safe; hammering four machines at once causes
            // connection timeouts that look like device faults.
            foreach (var id in deviceIds)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await SyncDeviceAsync(id, ct));
            }

            var ok = results.Count(r => r.Success);
            _logger.LogInformation(
                "Sync round finished: {Ok}/{Total} devices, {Inserted} new punches",
                ok, results.Count, results.Sum(r => r.Inserted));

            return results;
        }

        public async Task<DeviceSyncResult> SyncDeviceAsync(int deviceId, CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device is null)
                return new DeviceSyncResult(deviceId, "?", false, 0, 0, 0, 0, "Device not found");

            var syncLog = new SyncLog
            {
                DeviceId = device.DeviceId,
                BranchId = device.BranchId,
                StartTime = DateTime.Now,
                Status = "Running"
            };
            db.SyncLogs.Add(syncLog);
            await db.SaveChangesAsync(ct);

            try
            {
                // ── 1. Connect ─────────────────────────────────────────────
                using var reader = _readerFactory();

                if (!await reader.ConnectAsync(device.DeviceIP, device.DevicePort, device.CommPassword))
                    throw new InvalidOperationException(
                        $"Could not connect to {device.DeviceIP}:{device.DevicePort}");

                device.IsOnline = true;
                device.ConnectionStatus = "Connected";
                device.LastConnectionTime = DateTime.Now;

                // ── 2. Align the clock ─────────────────────────────────────
                // Do this BEFORE reading. A branch machine running four minutes
                // slow makes the same arrival look like two different times.
                var info = await reader.GetDeviceInfoAsync();
                if (info is not null)
                {
                    var drift = (DateTime.Now - info.DeviceTime).TotalSeconds;
                    if (Math.Abs(drift) > 60)
                    {
                        _logger.LogWarning(
                            "Device {Name} clock is off by {Drift:F0}s - correcting",
                            device.DeviceName, drift);
                        await reader.SetDeviceTimeAsync(DateTime.Now);
                    }
                }

                // ── 3. Read the log ────────────────────────────────────────
                var since = device.LastConnectionTime?.AddDays(-2);   // small overlap on purpose
                var punches = await reader.GetAttendanceLogsAsync(since);

                // ── 4. Resolve biometric IDs to employees ──────────────────
                // The device says "1017". Only EmployeeDevices knows that
                // 1017 ON THIS DEVICE means Ramesh. The same 1017 on another
                // device may be somebody else entirely.
                var map = await db.EmployeeDevices
                    .Where(ed => ed.DeviceId == device.DeviceId && ed.IsActive)
                    .ToDictionaryAsync(ed => ed.BiometricUserId, ed => ed.EmployeeId, ct);

                // ── 5. Load what we already have, to skip the obvious repeats ──
                var earliest = punches.Count > 0 ? punches.Min(p => p.PunchTime).Date : DateTime.Today;

                var existing = await db.AttendanceLogs
                    .Where(a => a.DeviceId == device.DeviceId && a.AttendanceTime >= earliest)
                    .Select(a => new { a.BiometricUserId, a.AttendanceTime })
                    .ToListAsync(ct);

                var existingSet = existing
                    .Select(e => (e.BiometricUserId, e.AttendanceTime))
                    .ToHashSet();

                var inserted = 0;
                var duplicates = 0;
                var unmapped = 0;

                foreach (var p in punches.OrderBy(p => p.PunchTime))
                {
                    if (existingSet.Contains((p.BiometricUserId, p.PunchTime)))
                    {
                        duplicates++;
                        continue;
                    }

                    // Minimum-interval rule: same person, same device, within 60s.
                    if (existingSet.Any(e =>
                            e.BiometricUserId == p.BiometricUserId &&
                            (p.PunchTime - e.AttendanceTime).Duration() < MinimumInterval))
                    {
                        duplicates++;
                        continue;
                    }

                    int? employeeId = null;
                    if (map.TryGetValue(p.BiometricUserId, out var id))
                        employeeId = id;
                    else
                        unmapped++;

                    db.AttendanceLogs.Add(new AttendanceLog
                    {
                        BiometricUserId = p.BiometricUserId,
                        EmployeeId = employeeId,
                        DeviceId = device.DeviceId,
                        BranchId = device.BranchId,
                        AttendanceTime = p.PunchTime,      // AD, exactly as the device said
                        AttendanceType = DescribeInOut(p.InOutMode),
                        VerifyMethod = DescribeVerify(p.VerifyMode),
                        WorkCode = p.WorkCode,
                        IsSynced = true,
                        SyncedDate = DateTime.Now,
                        IsProcessed = false,
                        IsManual = false,
                        CreatedDate = DateTime.Now
                    });

                    existingSet.Add((p.BiometricUserId, p.PunchTime));
                    inserted++;
                }

                // ── 6. Save ────────────────────────────────────────────────
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // The index did its job. Something raced us or the overlap
                    // window caught a record we had not loaded. Not an error.
                    _logger.LogInformation(
                        "Unique index rejected duplicate punches for {Name}", device.DeviceName);
                    duplicates += inserted;
                    inserted = 0;
                }

                await reader.DisconnectAsync();

                syncLog.EndTime = DateTime.Now;
                syncLog.Status = "Success";
                syncLog.RecordCount = punches.Count;
                syncLog.NewRecordCount = inserted;
                syncLog.DuplicateCount = duplicates;

                device.LastCheckTime = DateTime.Now;
                await db.SaveChangesAsync(ct);

                if (unmapped > 0)
                    _logger.LogWarning(
                        "{Count} punches from {Name} have no employee mapping - " +
                        "check Employees > Unregistered Biometric IDs",
                        unmapped, device.DeviceName);

                return new DeviceSyncResult(
                    device.DeviceId, device.DeviceName, true,
                    punches.Count, inserted, duplicates, unmapped, null);
            }
            catch (Exception ex)
            {
                // One device down must not stop the round.
                _logger.LogError(ex, "Sync failed for device {Name}", device.DeviceName);

                device.IsOnline = false;
                device.ConnectionStatus = "Error";
                device.LastCheckTime = DateTime.Now;

                syncLog.EndTime = DateTime.Now;
                syncLog.Status = "Failed";
                syncLog.ErrorMessage = Truncate(ex.Message, 500);

                db.DeviceErrors.Add(new DeviceError
                {
                    DeviceId = device.DeviceId,
                    BranchId = device.BranchId,
                    ErrorMessage = Truncate(ex.Message, 500),
                    Severity = "High",
                    ErrorDateTime = DateTime.Now
                });

                await db.SaveChangesAsync(ct);

                return new DeviceSyncResult(
                    device.DeviceId, device.DeviceName, false, 0, 0, 0, 0, ex.Message);
            }
        }

        private static string DescribeInOut(int mode) => mode switch
        {
            0 => "CheckIn",
            1 => "CheckOut",
            2 => "BreakOut",
            3 => "BreakIn",
            4 => "OvertimeIn",
            5 => "OvertimeOut",
            _ => "Unknown"
        };

        private static string DescribeVerify(int mode) => mode switch
        {
            0 => "Password",
            1 => "Fingerprint",
            2 => "Card",
            15 => "Face",
            _ => "Other"
        };

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is Microsoft.Data.SqlClient.SqlException sql &&
            (sql.Number == 2601 || sql.Number == 2627);

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max];
    }
}
