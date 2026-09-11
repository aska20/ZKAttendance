using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    public class AgentLoginRequest
    {
        public string AgentKey { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public string? AgentVersion { get; set; }
    }

    public class AgentPunch
    {
        /// <summary>The enrol number the device reported.</summary>
        public string BiometricUserId { get; set; } = string.Empty;
        public int DeviceId { get; set; }
        public DateTime PunchTime { get; set; }
        public int VerifyMode { get; set; }
        public int InOutMode { get; set; }
        public int WorkCode { get; set; }
    }

    public class AgentPunchBatch
    {
        public string AgentKey { get; set; } = string.Empty;
        public List<AgentPunch> Punches { get; set; } = new();
    }

    /// <summary>
    /// Everything App2 (the local agent) is allowed to call.
    ///
    /// WHY THIS IS SEPARATE FROM THE REST OF THE API
    /// ---------------------------------------------
    /// The normal endpoints are for people, authenticated with a JWT from a
    /// login screen. These are for a machine sitting in an office that nobody
    /// logs into. It authenticates with an agent key and secret instead, and it
    /// can only touch its own branch's devices.
    ///
    /// [AllowAnonymous] on the class is not "no security" — it means "not the
    /// user JWT scheme". Every method verifies the agent key itself, and an
    /// unknown or inactive key gets nothing.
    ///
    /// THE INGEST PATH IS DELIBERATELY THIN
    /// ------------------------------------
    /// This controller does not calculate anything. It writes punches into
    /// AttendanceLogs and stops. Late, absent, half-day and approvals are all
    /// decided later by the existing services, so there is exactly one place
    /// where attendance logic lives and the agent cannot disagree with it.
    /// </summary>
    [Route("api/Agent")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Agent")]
    [AllowAnonymous]
    public class AgentApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<AgentApiController> _logger;

        public AgentApiController(AttendanceDbContext db, ILogger<AgentApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Confirms the agent key and secret, and records that this agent is
        /// alive. App2 calls this at startup and after every reconnect.
        /// </summary>
        [HttpPost("login")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Login([FromBody] AgentLoginRequest request, CancellationToken ct)
        {
            var agent = await Authenticate(request.AgentKey, request.Secret, ct);
            if (agent is null)
            {
                // Deliberately vague. Telling an attacker which half was wrong
                // turns one guess into two cheaper ones.
                _logger.LogWarning("Agent login refused for key {Key} from {Ip}",
                    Truncate(request.AgentKey), RemoteIp());
                return Unauthorized(ApiError.From("Agent key or secret is not valid."));
            }

            agent.IsConnected = true;
            agent.LastConnectedAt = DateTime.Now;
            agent.LastHeartbeatAt = DateTime.Now;
            agent.LastRemoteIp = RemoteIp();
            agent.AgentVersion = request.AgentVersion;
            agent.ModifiedDate = DateTime.Now;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Agent '{Name}' connected from {Ip}", agent.ServerName, agent.LastRemoteIp);

            return Ok(new
            {
                agent.LocalServerId,
                agent.ServerName,
                agent.BranchId,
                branchName = await _db.Branches
                    .Where(b => b.BranchId == agent.BranchId)
                    .Select(b => b.BranchName)
                    .FirstOrDefaultAsync(ct),
                serverTime = DateTime.Now,
                message = "Connected."
            });
        }

        /// <summary>Keeps the connection state honest between syncs.</summary>
        [HttpPost("heartbeat")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Heartbeat([FromBody] AgentLoginRequest request, CancellationToken ct)
        {
            var agent = await Authenticate(request.AgentKey, request.Secret, ct);
            if (agent is null) return Unauthorized(ApiError.From("Agent key or secret is not valid."));

            agent.IsConnected = true;
            agent.LastHeartbeatAt = DateTime.Now;
            agent.LastRemoteIp = RemoteIp();
            await _db.SaveChangesAsync(ct);

            return Ok(new { serverTime = DateTime.Now });
        }

        /// <summary>
        /// The devices this agent is responsible for.
        ///
        /// Scoped two ways: devices explicitly assigned to this agent, plus any
        /// unassigned device in its branch. The second half means an existing
        /// installation works the moment an agent is registered, without having
        /// to reassign every device by hand.
        /// </summary>
        [HttpGet("devices")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Devices(
            [FromQuery] string agentKey, [FromQuery] string secret, CancellationToken ct)
        {
            var agent = await Authenticate(agentKey, secret, ct);
            if (agent is null) return Unauthorized(ApiError.From("Agent key or secret is not valid."));

            var devices = await _db.Devices
                .Where(d => d.IsActive
                            && (d.LocalServerId == agent.LocalServerId
                                || (d.LocalServerId == null && d.BranchId == agent.BranchId)))
                .OrderBy(d => d.DeviceName)
                .Select(d => new
                {
                    d.DeviceId,
                    d.DeviceName,
                    d.DeviceIP,
                    d.DevicePort,
                    d.CommPassword,
                    d.SerialNumber,
                    d.DeviceModel,
                    role = d.Role.ToString(),
                    d.IsOnline,
                    d.LastConnectionTime
                })
                .ToListAsync(ct);

            return Ok(new { agent.LocalServerId, deviceCount = devices.Count, devices });
        }

        /// <summary>
        /// Receive a batch of punches from the agent's outbox.
        ///
        /// Returns a per-punch outcome so the agent knows exactly which rows it
        /// may mark complete. A duplicate counts as accepted: the agent did its
        /// job and must not keep retrying.
        /// </summary>
        [HttpPost("punches")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Punches(
            [FromHeader(Name = "X-Agent-Secret")] string? headerSecret,
            [FromBody] AgentPunchBatch batch,
            CancellationToken ct)
        {
            var agent = await Authenticate(batch.AgentKey, headerSecret ?? "", ct);
            if (agent is null) return Unauthorized(ApiError.From("Agent key or secret is not valid."));

            if (batch.Punches.Count == 0)
                return Ok(new { accepted = 0, duplicates = 0, rejected = 0 });

            if (batch.Punches.Count > 1000)
                return BadRequest(ApiError.From("Send at most 1000 punches per batch."));

            // The agent may only write for devices it owns. Without this, a
            // leaked key from one branch could post attendance for another.
            var allowedDevices = await _db.Devices
                .Where(d => d.LocalServerId == agent.LocalServerId
                            || (d.LocalServerId == null && d.BranchId == agent.BranchId))
                .Select(d => d.DeviceId)
                .ToListAsync(ct);
            var allowed = allowedDevices.ToHashSet();

            var biometricIds = batch.Punches.Select(p => p.BiometricUserId).Distinct().ToList();

            // Enrol number to employee, per device, same as the existing sync.
            var links = await _db.EmployeeDevices
                .Where(ed => ed.IsActive && allowed.Contains(ed.DeviceId)
                             && biometricIds.Contains(ed.BiometricUserId))
                .Select(ed => new { ed.DeviceId, ed.BiometricUserId, ed.EmployeeId })
                .ToListAsync(ct);

            var byDevice = links.ToDictionary(x => (x.DeviceId, x.BiometricUserId), x => x.EmployeeId);

            var fallback = await _db.Employees
                .Where(e => biometricIds.Contains(e.BiometricUserId))
                .Select(e => new { e.BiometricUserId, e.EmployeeId })
                .ToListAsync(ct);
            var byNumber = fallback
                .GroupBy(x => x.BiometricUserId)
                .ToDictionary(g => g.Key, g => g.First().EmployeeId);

            int accepted = 0, duplicates = 0, rejected = 0;
            var results = new List<object>();

            foreach (var p in batch.Punches)
            {
                if (!allowed.Contains(p.DeviceId))
                {
                    rejected++;
                    results.Add(new { p.BiometricUserId, p.PunchTime, status = "rejected", reason = "device not owned by this agent" });
                    continue;
                }

                // The unique index is the real guarantee; this check just saves
                // a round trip for the common case.
                var exists = await _db.AttendanceLogs.AnyAsync(
                    a => a.BiometricUserId == p.BiometricUserId
                         && a.AttendanceTime == p.PunchTime
                         && a.DeviceId == p.DeviceId, ct);

                if (exists)
                {
                    duplicates++;
                    results.Add(new { p.BiometricUserId, p.PunchTime, status = "duplicate" });
                    continue;
                }

                var branchId = await _db.Devices
                    .Where(d => d.DeviceId == p.DeviceId)
                    .Select(d => d.BranchId)
                    .FirstOrDefaultAsync(ct);

                // EmployeeId stays null when nobody claims the number. The punch
                // is kept rather than dropped, and shows up as an unmapped ID.
                var employeeId = byDevice.TryGetValue((p.DeviceId, p.BiometricUserId), out var eid)
                    ? eid
                    : byNumber.TryGetValue(p.BiometricUserId, out var fid) ? fid : (int?)null;

                _db.AttendanceLogs.Add(new AttendanceLog
                {
                    BiometricUserId = p.BiometricUserId,
                    EmployeeId = employeeId,
                    DeviceId = p.DeviceId,
                    BranchId = branchId,
                    AttendanceTime = p.PunchTime,
                    AttendanceType = MapInOut(p.InOutMode),
                    VerifyMethod = MapVerify(p.VerifyMode),
                    WorkCode = p.WorkCode,
                    IsSynced = true,
                    SyncedDate = DateTime.Now,
                    IsManual = false,
                    CreatedDate = DateTime.Now
                });

                accepted++;
                results.Add(new { p.BiometricUserId, p.PunchTime, status = "accepted" });
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // The unique index refused something that slipped past the check
                // above, which happens when two batches overlap. Not an error the
                // agent can act on, so report it as duplicates rather than fail
                // the whole batch and cause an endless retry.
                _logger.LogWarning(ex, "Punch batch from '{Agent}' hit the unique index", agent.ServerName);
                return Ok(new
                {
                    accepted = 0,
                    duplicates = batch.Punches.Count,
                    rejected = 0,
                    message = "Already recorded."
                });
            }

            agent.LastImportAt = DateTime.Now;
            agent.RecordsImported += accepted;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Agent '{Agent}' imported {Accepted} punch(es), {Dup} duplicate, {Rej} rejected",
                agent.ServerName, accepted, duplicates, rejected);

            return Ok(new { accepted, duplicates, rejected, results });
        }

        /// <summary>A short attendance summary the agent can display locally.</summary>
        [HttpGet("summary")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Summary(
            [FromQuery] string agentKey,
            [FromQuery] string secret,
            [FromQuery] DateTime? date,
            CancellationToken ct)
        {
            var agent = await Authenticate(agentKey, secret, ct);
            if (agent is null) return Unauthorized(ApiError.From("Agent key or secret is not valid."));

            var day = (date ?? DateTime.Today).Date;

            var punches = await _db.AttendanceLogs
                .Where(a => a.BranchId == agent.BranchId
                            && a.AttendanceTime >= day
                            && a.AttendanceTime < day.AddDays(1))
                .ToListAsync(ct);

            return Ok(new
            {
                date = day.ToString("yyyy-MM-dd"),
                agent.BranchId,
                totalPunches = punches.Count,
                distinctEmployees = punches.Select(p => p.BiometricUserId).Distinct().Count(),
                unmapped = punches.Count(p => p.EmployeeId == null),
                lastPunchAt = punches.Count > 0 ? punches.Max(p => p.AttendanceTime) : (DateTime?)null
            });
        }

        // ── helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the key and secret. The secret is compared against a salted
        /// hash, never stored in the clear, so a database leak cannot be used to
        /// impersonate a branch and inject attendance.
        /// </summary>
        private async Task<LocalServer?> Authenticate(string? key, string? secret, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret)) return null;

            var agent = await _db.LocalServers
                .FirstOrDefaultAsync(a => a.AgentKey == key && a.IsActive, ct);

            if (agent is null) return null;

            return VerifySecret(secret, agent.SecretSalt, agent.SecretHash) ? agent : null;
        }

        internal static bool VerifySecret(string secret, string salt, string expectedHash)
        {
            var actual = HashSecret(secret, salt);

            // Fixed-time compare. A normal string comparison returns faster on an
            // early mismatch, which leaks the secret one character at a time.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actual),
                Encoding.UTF8.GetBytes(expectedHash));
        }

        internal static string HashSecret(string secret, string salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                secret, Encoding.UTF8.GetBytes(salt), 100_000, HashAlgorithmName.SHA256);
            return Convert.ToBase64String(pbkdf2.GetBytes(32));
        }

        private string RemoteIp() =>
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        private static string Truncate(string? s) =>
            string.IsNullOrEmpty(s) ? "(none)" : s.Length <= 8 ? s : s[..8] + "...";

        private static string MapInOut(int mode) => mode switch
        {
            0 => "Check In",
            1 => "Check Out",
            2 => "Break Out",
            3 => "Break In",
            4 => "Overtime In",
            5 => "Overtime Out",
            _ => "Unknown"
        };

        private static string MapVerify(int mode) => mode switch
        {
            0 => "Password",
            1 => "Fingerprint",
            2 => "Card",
            15 => "Face",
            _ => "Unknown"
        };
    }

    public class RegisterAgentRequest
    {
        public string ServerName { get; set; } = string.Empty;
        public int BranchId { get; set; }
    }

    /// <summary>
    /// Managing local agents from App1. Admin only, and separate from the
    /// agent's own endpoints so the two never share an auth path.
    /// </summary>
    [Route("api/LocalServers")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Agent")]
    [Authorize(Roles = ZKAttendance.Api.Security.Roles.Admin)]
    public class LocalServersApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<LocalServersApiController> _logger;

        public LocalServersApiController(AttendanceDbContext db, ILogger<LocalServersApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Registered agents and whether they are talking to us.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var agents = await _db.LocalServers
                .Include(a => a.Branch)
                .OrderBy(a => a.ServerName)
                .ToListAsync(ct);

            var deviceCounts = await _db.Devices
                .Where(d => d.LocalServerId != null)
                .GroupBy(d => d.LocalServerId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

            return Ok(agents.Select(a => new
            {
                a.LocalServerId,
                a.ServerName,
                a.AgentKey,
                a.BranchId,
                branchName = a.Branch?.BranchName,
                a.IsActive,
                // "Connected" is only meaningful if we heard from it recently.
                // A crashed agent never sends a disconnect.
                isConnected = a.IsConnected
                              && a.LastHeartbeatAt != null
                              && a.LastHeartbeatAt > DateTime.Now.AddMinutes(-5),
                a.LastConnectedAt,
                a.LastHeartbeatAt,
                a.LastImportAt,
                a.LastRemoteIp,
                a.AgentVersion,
                a.RecordsImported,
                deviceCount = deviceCounts.GetValueOrDefault(a.LocalServerId)
            }));
        }

        /// <summary>
        /// Create an agent and issue its credentials.
        ///
        /// The secret is returned EXACTLY ONCE, here. Only its hash is stored,
        /// so it cannot be shown again. Losing it means issuing a new one.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Register([FromBody] RegisterAgentRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ServerName))
                return BadRequest(ApiError.From("A name is required, for example 'Head office agent'."));

            if (!await _db.Branches.AnyAsync(b => b.BranchId == request.BranchId, ct))
                return BadRequest(ApiError.From("That branch does not exist."));

            var agentKey = "agt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

            var agent = new LocalServer
            {
                ServerName = request.ServerName.Trim(),
                BranchId = request.BranchId,
                AgentKey = agentKey,
                SecretSalt = salt,
                SecretHash = AgentApiController.HashSecret(secret, salt),
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _db.LocalServers.Add(agent);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Agent '{Name}' registered by {User}", agent.ServerName, User.Identity?.Name);

            return StatusCode(201, new
            {
                agent.LocalServerId,
                agent.ServerName,
                agentKey,
                secret,
                warning = "Copy the secret now. It is stored only as a hash and cannot be shown again."
            });
        }

        /// <summary>Issue a fresh secret, for instance after the old one leaked.</summary>
        [HttpPost("{id:int}/rotate-secret")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Rotate(int id, CancellationToken ct)
        {
            var agent = await _db.LocalServers.FirstOrDefaultAsync(a => a.LocalServerId == id, ct);
            if (agent is null) return NotFound(ApiError.From("That agent no longer exists."));

            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

            agent.SecretSalt = salt;
            agent.SecretHash = AgentApiController.HashSecret(secret, salt);
            agent.IsConnected = false;
            agent.ModifiedDate = DateTime.Now;
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Agent '{Name}' secret rotated by {User}", agent.ServerName, User.Identity?.Name);

            return Ok(new
            {
                agent.AgentKey,
                secret,
                warning = "The old secret stopped working immediately. Update App2 before its next sync."
            });
        }

        /// <summary>Point a device at an agent, or clear it with a null agent id.</summary>
        [HttpPost("assign-device")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> AssignDevice(
            [FromQuery] int deviceId, [FromQuery] int? localServerId, CancellationToken ct)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device is null) return NotFound(ApiError.From("That device no longer exists."));

            if (localServerId.HasValue
                && !await _db.LocalServers.AnyAsync(a => a.LocalServerId == localServerId.Value, ct))
                return NotFound(ApiError.From("That agent no longer exists."));

            device.LocalServerId = localServerId;
            device.ModifiedDate = DateTime.Now;
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                deviceId,
                localServerId,
                message = localServerId.HasValue
                    ? "Device assigned. The agent will pick it up on its next device list."
                    : "Device unassigned. It falls back to being polled directly by this server."
            });
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var agent = await _db.LocalServers.FirstOrDefaultAsync(a => a.LocalServerId == id, ct);
            if (agent is null) return NotFound(ApiError.From("That agent no longer exists."));

            // Devices keep working; they just go back to being polled directly.
            var devices = await _db.Devices.Where(d => d.LocalServerId == id).ToListAsync(ct);
            foreach (var d in devices) d.LocalServerId = null;

            _db.LocalServers.Remove(agent);
            await _db.SaveChangesAsync(ct);

            return Ok(new { deleted = true, devicesUnassigned = devices.Count });
        }
    }
}
