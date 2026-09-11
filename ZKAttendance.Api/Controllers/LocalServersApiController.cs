using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Management of remote office agents (App2 / ZKAttendance.Agent).
    /// Management role only.
    /// </summary>
    [Route("api/LocalServers")]
    [ApiController]
    [Produces("application/json")]
    [Tags("LocalServers")]
    [Authorize(Roles = Roles.Management)]
    public class LocalServersApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<LocalServersApiController> _logger;

        public LocalServersApiController(AttendanceDbContext db, ILogger<LocalServersApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>List all registered local agents.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var servers = await _db.LocalServers
                .Include(s => s.Branch)
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => new
                {
                    s.LocalServerId,
                    s.ServerName,
                    s.BranchId,
                    BranchName = s.Branch != null ? s.Branch.BranchName : null,
                    s.AgentKey,
                    s.IsActive,
                    s.LastHeartbeatAt,
                    isOnline = s.LastHeartbeatAt.HasValue && s.LastHeartbeatAt.Value >= DateTime.Now.AddMinutes(-5),
                    s.AgentVersion,
                    s.CreatedDate
                })
                .ToListAsync(ct);

            return Ok(servers);
        }

        /// <summary>Get details of one registered agent.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var server = await _db.LocalServers
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.LocalServerId == id, ct);

            if (server is null)
                return NotFound(new { message = $"LocalServer {id} not found." });

            return Ok(new
            {
                server.LocalServerId,
                server.ServerName,
                server.BranchId,
                BranchName = server.Branch?.BranchName,
                server.AgentKey,
                server.IsActive,
                server.LastHeartbeatAt,
                isOnline = server.LastHeartbeatAt.HasValue && server.LastHeartbeatAt.Value >= DateTime.Now.AddMinutes(-5),
                server.AgentVersion,
                server.CreatedDate
            });
        }

        /// <summary>
        /// Register a new agent. Returns the plaintext secret ONCE.
        /// Copy it into the agent's appsettings.json.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterLocalServerRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.ServerName))
                return BadRequest(new { message = "Server name is required." });

            var branchExists = await _db.Branches.AnyAsync(b => b.BranchId == req.BranchId, ct);
            if (!branchExists)
                return BadRequest(new { message = $"Branch {req.BranchId} does not exist." });

            // Generate unique agent key: agt_<random12>
            string agentKey;
            do
            {
                agentKey = "agt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            }
            while (await _db.LocalServers.AnyAsync(s => s.AgentKey == agentKey, ct));

            // Generate secure secret: 32 random bytes hex
            var rawSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var secretHash = AgentController.HashSecret(rawSecret);

            var server = new LocalServer
            {
                ServerName = req.ServerName.Trim(),
                BranchId = req.BranchId,
                AgentKey = agentKey,
                SecretHash = secretHash,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _db.LocalServers.Add(server);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Registered new agent {Key} for branch {BranchId}", agentKey, req.BranchId);

            return CreatedAtAction(nameof(Get), new { id = server.LocalServerId }, new
            {
                server.LocalServerId,
                server.ServerName,
                server.BranchId,
                server.AgentKey,
                secret = rawSecret,
                message = "Agent registered successfully. Save this secret now; it will NOT be shown again."
            });
        }

        /// <summary>
        /// Rotate the secret for an existing agent. The old secret is immediately invalidated.
        /// </summary>
        [HttpPost("{id:int}/rotate-secret")]
        public async Task<IActionResult> RotateSecret(int id, CancellationToken ct)
        {
            var server = await _db.LocalServers.FindAsync(new object[] { id }, ct);
            if (server is null)
                return NotFound(new { message = $"LocalServer {id} not found." });

            var rawSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            server.SecretHash = AgentController.HashSecret(rawSecret);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Rotated secret for agent {Key}", server.AgentKey);

            return Ok(new
            {
                server.LocalServerId,
                server.AgentKey,
                secret = rawSecret,
                message = "New secret generated. Update the agent configuration immediately."
            });
        }

        /// <summary>Update agent details.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateLocalServerRequest req, CancellationToken ct)
        {
            var server = await _db.LocalServers.FindAsync(new object[] { id }, ct);
            if (server is null)
                return NotFound(new { message = $"LocalServer {id} not found." });

            if (req.BranchId.HasValue && req.BranchId.Value != server.BranchId)
            {
                var branchExists = await _db.Branches.AnyAsync(b => b.BranchId == req.BranchId.Value, ct);
                if (!branchExists)
                    return BadRequest(new { message = $"Branch {req.BranchId.Value} does not exist." });

                server.BranchId = req.BranchId.Value;
            }

            if (!string.IsNullOrWhiteSpace(req.ServerName))
                server.ServerName = req.ServerName.Trim();

            if (req.IsActive.HasValue)
                server.IsActive = req.IsActive.Value;

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Agent updated.", server.LocalServerId });
        }

        /// <summary>Deactivate or delete an agent.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var server = await _db.LocalServers.FindAsync(new object[] { id }, ct);
            if (server is null)
                return NotFound(new { message = $"LocalServer {id} not found." });

            _db.LocalServers.Remove(server);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Deleted agent {Key}", server.AgentKey);
            return Ok(new { message = "Agent deleted." });
        }
    }

    public record RegisterLocalServerRequest(string ServerName, int BranchId);
    public record UpdateLocalServerRequest(string? ServerName, int? BranchId, bool? IsActive);
}
