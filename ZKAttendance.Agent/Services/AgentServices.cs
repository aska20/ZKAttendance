using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Agent.Data;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Agent.Services
{
    // ── what App1 sends back ────────────────────────────────────────

    public record CentralDevice(
        int DeviceId, string DeviceName, string DeviceIP, int DevicePort,
        int CommPassword, string? SerialNumber, string? DeviceModel,
        string? Role, bool IsOnline, DateTime? LastConnectionTime);

    public record DeviceListResponse(int LocalServerId, int DeviceCount, List<CentralDevice> Devices);

    public record AgentLoginResponse(
        int LocalServerId, string ServerName, int BranchId,
        string? BranchName, DateTime ServerTime, string Message);

    public record PunchBatchResponse(int Accepted, int Duplicates, int Rejected);

    /// <summary>
    /// Everything App2 says to App1.
    ///
    /// Uses IHttpClientFactory rather than a hand-made HttpClient. A new client
    /// per call exhausts sockets under load; one static client never notices a
    /// DNS change. The factory pools and rotates handlers, which fixes both.
    /// </summary>
    public interface ICentralClient
    {
        Task<AgentLoginResponse?> LoginAsync(CancellationToken ct = default);
        Task<bool> HeartbeatAsync(CancellationToken ct = default);
        Task<DeviceListResponse?> GetDevicesAsync(CancellationToken ct = default);
        Task<(bool ok, PunchBatchResponse? result, string? error, bool permanent)>
            SendPunchesAsync(IEnumerable<OutboxPunch> punches, CancellationToken ct = default);
    }

    public class CentralClient : ICentralClient
    {
        public const string HttpClientName = "central";

        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;
        private readonly ILogger<CentralClient> _logger;

        public CentralClient(
            IHttpClientFactory factory, IConfiguration config, ILogger<CentralClient> logger)
        {
            _factory = factory;
            _config = config;
            _logger = logger;
        }

        private string AgentKey => _config["Central:AgentKey"] ?? "";
        private string Secret => _config["Central:Secret"] ?? "";

        public async Task<AgentLoginResponse?> LoginAsync(CancellationToken ct = default)
        {
            var client = _factory.CreateClient(HttpClientName);
            try
            {
                var response = await client.PostAsJsonAsync("api/Agent/login", new
                {
                    agentKey = AgentKey,
                    secret = Secret,
                    agentVersion = typeof(CentralClient).Assembly.GetName().Version?.ToString() ?? "1.0"
                }, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Central refused the login: HTTP {Code}", (int)response.StatusCode);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<AgentLoginResponse>(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reach the central server");
                return null;
            }
        }

        public async Task<bool> HeartbeatAsync(CancellationToken ct = default)
        {
            var client = _factory.CreateClient(HttpClientName);
            try
            {
                var response = await client.PostAsJsonAsync("api/Agent/heartbeat",
                    new { agentKey = AgentKey, secret = Secret }, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<DeviceListResponse?> GetDevicesAsync(CancellationToken ct = default)
        {
            var client = _factory.CreateClient(HttpClientName);
            try
            {
                var url = $"api/Agent/devices?agentKey={Uri.EscapeDataString(AgentKey)}" +
                          $"&secret={Uri.EscapeDataString(Secret)}";
                return await client.GetFromJsonAsync<DeviceListResponse>(url, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch the device list");
                return null;
            }
        }

        public async Task<(bool ok, PunchBatchResponse? result, string? error, bool permanent)>
            SendPunchesAsync(IEnumerable<OutboxPunch> punches, CancellationToken ct = default)
        {
            var client = _factory.CreateClient(HttpClientName);

            var body = new
            {
                agentKey = AgentKey,
                punches = punches.Select(p => new
                {
                    biometricUserId = p.BiometricUserId,
                    deviceId = p.DeviceId,
                    punchTime = p.PunchTime,
                    verifyMode = p.VerifyMode,
                    inOutMode = p.InOutMode,
                    workCode = p.WorkCode
                })
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/Agent/punches")
                {
                    Content = JsonContent.Create(body)
                };
                // The secret travels in a header, not the query string, so it
                // does not end up in the server's access log.
                request.Headers.Add("X-Agent-Secret", Secret);

                var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PunchBatchResponse>(cancellationToken: ct);
                    return (true, result, null, false);
                }

                var text = await response.Content.ReadAsStringAsync(ct);

                // 4xx means our payload is wrong and retrying will not help.
                // 5xx is their problem and is worth trying again.
                var permanent = (int)response.StatusCode is >= 400 and < 500
                                && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout
                                && response.StatusCode != System.Net.HttpStatusCode.TooManyRequests;

                return (false, null, $"HTTP {(int)response.StatusCode}: {Trim(text)}", permanent);
            }
            catch (Exception ex)
            {
                // Network down, DNS, timeout. Always worth retrying.
                return (false, null, ex.Message, false);
            }

            static string Trim(string s) => s.Length <= 200 ? s : s[..200];
        }
    }

    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The manual sync workflow:
    ///
    ///   choose device -> connect over the LAN -> read punches
    ///                 -> save to the outbox -> (worker posts to App1)
    ///
    /// Note what it does NOT do: it never decides anything about attendance.
    /// Late, absent, half-day and approvals are all App1's job. The agent
    /// fetches and forwards, which is what keeps one copy of the rules.
    /// </summary>
    public interface IDeviceSyncService
    {
        Task<SyncRun> SyncDeviceAsync(int deviceId, string? triggeredBy, CancellationToken ct = default);
    }

    public class DeviceSyncService : IDeviceSyncService
    {
        private readonly AgentDbContext _db;
        private readonly ICentralClient _central;
        private readonly Func<IZkDeviceReader> _readerFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<DeviceSyncService> _logger;

        public DeviceSyncService(
            AgentDbContext db,
            ICentralClient central,
            Func<IZkDeviceReader> readerFactory,
            IConfiguration config,
            ILogger<DeviceSyncService> logger)
        {
            _db = db;
            _central = central;
            _readerFactory = readerFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<SyncRun> SyncDeviceAsync(
            int deviceId, string? triggeredBy, CancellationToken ct = default)
        {
            // App1 owns the device list, so ask it rather than keeping a stale
            // copy here. One less thing that can drift.
            var list = await _central.GetDevicesAsync(ct);
            var device = list?.Devices.FirstOrDefault(d => d.DeviceId == deviceId);

            var run = new SyncRun
            {
                DeviceId = deviceId,
                DeviceName = device?.DeviceName ?? $"Device {deviceId}",
                StartedAt = DateTime.Now,
                TriggeredBy = triggeredBy,
                Status = "Running"
            };
            _db.SyncRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            if (device is null)
            {
                run.Status = "Failed";
                run.FinishedAt = DateTime.Now;
                run.Message = "The central server does not list this device for this agent. " +
                              "Check the device is active and assigned to this branch.";
                await _db.SaveChangesAsync(ct);
                return run;
            }

            using var reader = _readerFactory();

            try
            {
                if (!await reader.ConnectAsync(device.DeviceIP, device.DevicePort, device.CommPassword))
                {
                    run.Status = "Failed";
                    run.Message = $"Could not reach {device.DeviceName} at {device.DeviceIP}:{device.DevicePort}. " +
                                  "Check it is powered on and on this network.";
                    run.FinishedAt = DateTime.Now;
                    await _db.SaveChangesAsync(ct);
                    return run;
                }

                // Ask for a window rather than everything. Most ZKTeco models
                // cannot filter at all and hand back the whole log regardless,
                // which is fine: the outbox unique index drops what we already
                // have, and App1's unique index catches anything that slips by.
                var days = _config.GetValue("Sync:LookbackDays", 7);
                var since = DateTime.Today.AddDays(-days);

                var punches = await reader.GetAttendanceLogsAsync(since);
                await reader.DisconnectAsync();

                run.RecordsRead = punches.Count;

                var queued = 0;
                foreach (var p in punches)
                {
                    if (p.PunchTime < since) continue;

                    // Cheap in-memory guard. The unique index below is the real
                    // one, but checking here avoids a failed insert per row.
                    var exists = await _db.OutboxPunches.AnyAsync(
                        o => o.BiometricUserId == p.BiometricUserId
                             && o.PunchTime == p.PunchTime
                             && o.DeviceId == deviceId, ct);

                    if (exists) continue;

                    _db.OutboxPunches.Add(new OutboxPunch
                    {
                        BiometricUserId = p.BiometricUserId,
                        DeviceId = deviceId,
                        PunchTime = p.PunchTime,
                        VerifyMode = p.VerifyMode,
                        InOutMode = p.InOutMode,
                        WorkCode = p.WorkCode,
                        Status = OutboxStatus.Pending,
                        CreatedAt = DateTime.Now,
                        SyncRunId = run.SyncRunId
                    });
                    queued++;
                }

                await _db.SaveChangesAsync(ct);

                run.RecordsQueued = queued;
                run.Status = "Success";
                run.FinishedAt = DateTime.Now;
                run.Message = queued == 0
                    ? $"Read {punches.Count} record(s). Nothing new to send."
                    : $"Read {punches.Count} record(s), queued {queued} for the central server.";

                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Sync of {Device}: read {Read}, queued {Queued}",
                    device.DeviceName, punches.Count, queued);

                return run;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync of device {Id} failed", deviceId);
                run.Status = "Failed";
                run.Message = ex.Message;
                run.FinishedAt = DateTime.Now;
                await _db.SaveChangesAsync(ct);
                return run;
            }
            finally
            {
                try { await reader.DisconnectAsync(); } catch { /* already closed */ }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Empties the outbox into App1, and keeps trying when it cannot.
    ///
    /// Runs on a timer rather than being triggered by the sync, so punches
    /// queued while the link was down go out on their own once it returns. A
    /// human pressing Sync is not required for recovery.
    /// </summary>
    public class OutboxDrainService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _config;
        private readonly ILogger<OutboxDrainService> _logger;

        public OutboxDrainService(
            IServiceScopeFactory scopes, IConfiguration config, ILogger<OutboxDrainService> logger)
        {
            _scopes = scopes;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromSeconds(_config.GetValue("Sync:DrainIntervalSeconds", 20));
            var batchSize = _config.GetValue("Sync:BatchSize", 200);

            // Let the app finish starting before touching the network.
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                    var central = scope.ServiceProvider.GetRequiredService<ICentralClient>();

                    await DrainOnceAsync(db, central, batchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A failed drain must never bring the agent down.
                    _logger.LogError(ex, "Outbox drain failed; retrying next cycle");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        internal async Task DrainOnceAsync(
            AgentDbContext db, ICentralClient central, int batchSize, CancellationToken ct)
        {
            var now = DateTime.Now;

            var batch = await db.OutboxPunches
                .Where(o => o.Status == OutboxStatus.Pending
                            && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
                .OrderBy(o => o.OutboxId)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) return;

            var (ok, result, error, permanent) = await central.SendPunchesAsync(batch, ct);

            if (ok)
            {
                foreach (var row in batch)
                {
                    row.Status = OutboxStatus.Sent;
                    row.SentAt = DateTime.Now;
                    row.LastError = null;
                }

                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Outbox: sent {Count} ({Accepted} new, {Dup} already there)",
                    batch.Count, result?.Accepted ?? 0, result?.Duplicates ?? 0);
                return;
            }

            foreach (var row in batch)
            {
                row.Attempts++;
                row.LastError = error;

                // Give up only on a permanent rejection, and only after a few
                // tries. Everything else keeps retrying for ever, because "the
                // internet was down" must never lose a punch.
                row.Status = permanent && row.Attempts >= 3 ? OutboxStatus.Dead : OutboxStatus.Pending;
                row.NextAttemptAt = DateTime.Now.AddSeconds(Backoff(row.Attempts));
            }

            await db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Outbox: {Count} punch(es) not sent ({Error}). Retrying.", batch.Count, error);
        }

        /// <summary>2s, 4s, 8s... capped at 5 minutes.</summary>
        private static int Backoff(int attempts) =>
            Math.Min(300, (int)Math.Pow(2, Math.Min(attempts, 8)));
    }

    /// <summary>Tells App1 we are alive between syncs.</summary>
    public class HeartbeatService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<HeartbeatService> _logger;

        public HeartbeatService(IServiceScopeFactory scopes, ILogger<HeartbeatService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var central = scope.ServiceProvider.GetRequiredService<ICentralClient>();
                    await central.HeartbeatAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Heartbeat failed");
                }

                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
