using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Login accounts. This screen manages Admin / HR accounts — the people who
    /// run the dashboard. Employee logins are created from the Employees page
    /// (they must be attached to an employee record).
    /// </summary>
    [Route("api/Users")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Users")]
    [Authorize(Roles = Roles.Admin)]
    public class UsersApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<UsersApiController> _logger;

        public UsersApiController(AttendanceDbContext db, ILogger<UsersApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>All login accounts.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get()
        {
            var rows = await _db.ApiUsers
                .OrderByDescending(u => u.CreatedDate)
                .Select(u => new
                {
                    u.ApiUserId,
                    u.Username,
                    u.Email,
                    u.Role,
                    u.IsActive,
                    u.EmployeeId,
                    employeeName = u.EmployeeId != null
                        ? _db.Employees.Where(e => e.EmployeeId == u.EmployeeId).Select(e => e.EmployeeName).FirstOrDefault()
                        : null,
                    u.CreatedDate,
                    u.LastLoginDate
                })
                .ToListAsync();

            return Ok(rows);
        }

        /// <summary>Enable or disable a login. A disabled account cannot sign in.</summary>
        [HttpPost("{id:int}/set-active")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> SetActive(int id, [FromQuery] bool active)
        {
            var u = await _db.ApiUsers.FirstOrDefaultAsync(x => x.ApiUserId == id);
            if (u is null) return NotFound(ApiError.From("Account not found"));

            if (!active && u.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
                return BadRequest(ApiError.From("The bootstrap 'admin' account cannot be disabled."));

            u.IsActive = active;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Account {Username} {State} by {By}", u.Username, active ? "enabled" : "disabled", User.Identity?.Name);
            return Ok(new { id, isActive = active });
        }

        /// <summary>Change a login's role (Admin / HR / Employee).</summary>
        [HttpPost("{id:int}/set-role")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> SetRole(int id, [FromQuery] string role)
        {
            var normalized = new[] { "Admin", "HR", "Employee" }
                .FirstOrDefault(r => r.Equals(role, StringComparison.OrdinalIgnoreCase));
            if (normalized is null)
                return BadRequest(ApiError.From("Role must be Admin, HR or Employee."));

            var u = await _db.ApiUsers.FirstOrDefaultAsync(x => x.ApiUserId == id);
            if (u is null) return NotFound(ApiError.From("Account not found"));

            if (u.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) && normalized != "Admin")
                return BadRequest(ApiError.From("The bootstrap 'admin' account must stay Admin."));

            u.Role = normalized;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Account {Username} role set to {Role} by {By}", u.Username, normalized, User.Identity?.Name);
            return Ok(new { id, role = normalized });
        }
    }
}
