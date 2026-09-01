using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Device / integration error log. The sync and monitor background jobs
    /// write a row here whenever a device connection or read fails, so a bad
    /// IP, wrong port or wrong Comm Key surfaces in the UI instead of only in
    /// the console.
    /// </summary>
    [Route("api/ErrorLog")]
    [ApiController]
    [Produces("application/json")]
    [Tags("ErrorLog")]
    [Authorize(Roles = Roles.Management)]
    public class ErrorLogApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<ErrorLogApiController> _logger;

        public ErrorLogApiController(AttendanceDbContext db, ILogger<ErrorLogApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <param name="resolved">null = all, false = open only, true = resolved only.</param>
        /// <param name="deviceId">Filter to one device.</param>
        /// <param name="page">1-based; page size 100.</param>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get(
            [FromQuery] bool? resolved = false,
            [FromQuery] int? deviceId = null,
            [FromQuery] int page = 1)
        {
            const int pageSize = 100;
            var q = _db.DeviceErrors.AsNoTracking().Include(e => e.Device).AsQueryable();

            if (resolved.HasValue) q = q.Where(e => e.IsResolved == resolved.Value);
            if (deviceId.HasValue) q = q.Where(e => e.DeviceId == deviceId.Value);

            var total = await q.CountAsync();
            var rows = await q
                .OrderByDescending(e => e.ErrorDateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    e.ErrorId,
                    e.DeviceId,
                    device = e.Device != null ? e.Device.DeviceName : null,
                    deviceIp = e.Device != null ? e.Device.DeviceIP : null,
                    e.ErrorMessage,
                    e.ErrorCode,
                    e.Severity,
                    e.ErrorDateTime,
                    e.IsResolved,
                    e.ResolvedDateTime,
                    e.Resolution
                })
                .ToListAsync();

            return Ok(new { page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize), items = rows });
        }

        /// <summary>How many unresolved errors exist — for a nav badge.</summary>
        [HttpGet("open-count")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> OpenCount()
            => Ok(new { count = await _db.DeviceErrors.CountAsync(e => !e.IsResolved) });

        /// <summary>Mark one error resolved.</summary>
        [HttpPost("{id:long}/resolve")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Resolve(long id, [FromBody] ResolveErrorRequest? body = null)
        {
            var row = await _db.DeviceErrors.FirstOrDefaultAsync(e => e.ErrorId == id);
            if (row is null) return NotFound();

            row.IsResolved = true;
            row.ResolvedDateTime = DateTime.Now;
            row.ResolvedBy = User.Identity?.Name;
            row.Resolution = body?.Resolution;
            await _db.SaveChangesAsync();
            return Ok(new { id, resolved = true });
        }

        /// <summary>Delete all resolved errors. Admin only.</summary>
        [HttpPost("clear-resolved")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> ClearResolved()
        {
            var deleted = await _db.DeviceErrors.Where(e => e.IsResolved).ExecuteDeleteAsync();
            _logger.LogInformation("Cleared {Count} resolved error(s) by {User}", deleted, User.Identity?.Name);
            return Ok(new { deleted });
        }
    }

    public class ResolveErrorRequest
    {
        public string? Resolution { get; set; }
    }
}
