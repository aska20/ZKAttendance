using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Agent.Data;
using ZKAttendance.Agent.Services;

namespace ZKAttendance.Agent.Controllers
{
    /// <summary>
    /// The agent's own API, used by its local page.
    ///
    /// No authentication. This runs on a machine inside the office and is not
    /// exposed to the internet; the credential that matters is the agent
    /// secret it uses to talk to App1, which lives in config and never reaches
    /// the browser. If the agent is ever put on a public address, this needs a
    /// login in front of it.
    /// </summary>
    [Route("api/agent")]
    [ApiController]
    [Produces("application/json")]
    public class AgentController : ControllerBase
    {
        private readonly AgentDbContext _db;
        private readonly ICentralClient _central;
        private readonly IDeviceSyncService _sync;
        private readonly IConfiguration _config;
        private readonly ILogger<AgentController> _logger;

        public AgentController(
            AgentDbContext db,
            ICentralClient central,
            IDeviceSyncService sync,
            IConfiguration config,
            ILogger<AgentController> logger)
        {
            _db = db;
            _central = central;
            _sync = sync;
            _config = config;
            _logger = logger;
        }

        /// <summary>Is the agent configured, and can it see App1?</summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var configured = !string.IsNullOrWhiteSpace(_config["Central:AgentKey"])
                             && !string.IsNullOrWhiteSpace(_config["Central:Secret"]);

            if (!configured)
            {
                return Ok(new
                {
                    configured = false,
                    connected = false,
                    message = "No agent key set. Register an agent in App1, then paste its key and secret into appsettings.json."
                });
            }

            var login = await _central.LoginAsync(ct);

            var pending = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Pending, ct);
            var dead = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Dead, ct);
            var sent = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Sent, ct);

            return Ok(new
            {
                configured = true,
                connected = login is not null,
                centralUrl = _config["Central:BaseUrl"],
                serverName = login?.ServerName,
                branchName = login?.BranchName,
                serverTime = login?.ServerTime,
                outbox = new { pending, sent, dead },
                message = login is null
                    ? "Cannot reach the central server. Check the URL, the network, and that the key is still valid."
                    : "Connected."
            });
        }

        /// <summary>Devices App1 says this agent is responsible for.</summary>
        [HttpGet("devices")]
        public async Task<IActionResult> Devices(CancellationToken ct)
        {
            var list = await _central.GetDevicesAsync(ct);
            if (list is null)
                return StatusCode(502, new { message = "Could not fetch the device list from the central server." });

            return Ok(list);
        }

        /// <summary>
        /// Read one device and queue what it holds. This is the Sync button.
        /// </summary>
        [HttpPost("sync/{deviceId:int}")]
        public async Task<IActionResult> Sync(int deviceId, CancellationToken ct)
        {
            var run = await _sync.SyncDeviceAsync(deviceId, User.Identity?.Name ?? "local", ct);

            return Ok(new
            {
                run.SyncRunId,
                run.DeviceName,
                run.Status,
                run.RecordsRead,
                run.RecordsQueued,
                run.Message,
                run.StartedAt,
                run.FinishedAt
            });
        }

        /// <summary>Recent sync runs, newest first.</summary>
        [HttpGet("runs")]
        public async Task<IActionResult> Runs(CancellationToken ct)
            => Ok(await _db.SyncRuns
                .OrderByDescending(r => r.SyncRunId)
                .Take(25)
                .ToListAsync(ct));

        /// <summary>What is still waiting to reach App1.</summary>
        [HttpGet("outbox")]
        public async Task<IActionResult> Outbox([FromQuery] string status = "Pending", CancellationToken ct = default)
        {
            var query = _db.OutboxPunches.AsQueryable();

            if (Enum.TryParse<OutboxStatus>(status, true, out var parsed))
                query = query.Where(o => o.Status == parsed);

            var rows = await query
                .OrderByDescending(o => o.OutboxId)
                .Take(200)
                .Select(o => new
                {
                    o.OutboxId,
                    o.BiometricUserId,
                    o.DeviceId,
                    o.PunchTime,
                    status = o.Status.ToString(),
                    o.Attempts,
                    o.NextAttemptAt,
                    o.LastError,
                    o.CreatedAt,
                    o.SentAt
                })
                .ToListAsync(ct);

            return Ok(new
            {
                pending = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Pending, ct),
                sent = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Sent, ct),
                dead = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Dead, ct),
                rows
            });
        }

        /// <summary>
        /// Push the outbox now instead of waiting for the timer. Useful after
        /// the internet has just come back.
        /// </summary>
        [HttpPost("outbox/drain")]
        public async Task<IActionResult> Drain(
            [FromServices] ILoggerFactory loggerFactory,
            [FromServices] IServiceScopeFactory scopes,
            CancellationToken ct)
        {
            var worker = new OutboxDrainService(scopes, _config, loggerFactory.CreateLogger<OutboxDrainService>());
            await worker.DrainOnceAsync(_db, _central, 200, ct);

            return Ok(new
            {
                pending = await _db.OutboxPunches.CountAsync(o => o.Status == OutboxStatus.Pending, ct),
                message = "Drain attempted."
            });
        }

        /// <summary>
        /// Put dead rows back in the queue, for instance after fixing the
        /// mapping in App1 that caused them to be rejected.
        /// </summary>
        [HttpPost("outbox/retry-dead")]
        public async Task<IActionResult> RetryDead(CancellationToken ct)
        {
            var dead = await _db.OutboxPunches.Where(o => o.Status == OutboxStatus.Dead).ToListAsync(ct);

            foreach (var row in dead)
            {
                row.Status = OutboxStatus.Pending;
                row.Attempts = 0;
                row.NextAttemptAt = null;
                row.LastError = null;
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { requeued = dead.Count });
        }

        /// <summary>A short summary from App1, so the office can sanity-check today.</summary>
        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] DateTime? date, CancellationToken ct)
        {
            var client = HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>()
                                    .CreateClient(CentralClient.HttpClientName);
            try
            {
                var url = $"api/Agent/summary?agentKey={Uri.EscapeDataString(_config["Central:AgentKey"] ?? "")}" +
                          $"&secret={Uri.EscapeDataString(_config["Central:Secret"] ?? "")}" +
                          (date.HasValue ? $"&date={date:yyyy-MM-dd}" : "");

                var json = await client.GetStringAsync(url, ct);
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch the summary");
                return StatusCode(502, new { message = "Could not reach the central server." });
            }
        }
    }

    public record AgentLoginBody(string Username, string Password);

    /// <summary>
    /// Sign-in for the agent's own page.
    ///
    /// The credentials are checked by App1, not here. The agent has no user
    /// table of its own, which is the point: one place to add or remove a
    /// person, and no second password to keep in step.
    ///
    /// The token returned is App1's, so it also proves the person is real if
    /// the agent ever needs to call App1 on their behalf.
    /// </summary>
    [Route("api/agent")]
    [ApiController]
    [Produces("application/json")]
    public class AgentAuthController : ControllerBase
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<AgentAuthController> _logger;

        public AgentAuthController(IHttpClientFactory factory, ILogger<AgentAuthController> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] AgentLoginBody body, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
                return BadRequest(new { message = "Enter a username and password." });

            var client = _factory.CreateClient(Services.CentralClient.HttpClientName);

            try
            {
                var response = await client.PostAsJsonAsync("api/Auth/login",
                    new { username = body.Username, password = body.Password }, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Login refused for {User}", body.Username);
                    return Unauthorized(new { message = "Username or password is not correct." });
                }

                var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

                // App1 has changed its login shape before, so read defensively
                // rather than binding to a DTO that might not match.
                string? Read(params string[] names)
                {
                    foreach (var n in names)
                        if (payload.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                            return v.GetString();
                    return null;
                }

                return Ok(new
                {
                    token = Read("token", "accessToken", "jwt") ?? "",
                    username = Read("username", "userName") ?? body.Username,
                    role = Read("role") ?? "User"
                });
            }
            catch (Exception ex)
            {
                // A login failure and an unreachable server are different
                // problems, and the person needs to know which one this is.
                _logger.LogWarning(ex, "Could not reach the central server to sign in");
                return StatusCode(502, new
                {
                    message = "Cannot reach the central server, so sign-in is not possible. Check the network and Central:BaseUrl."
                });
            }
        }
    }
}
