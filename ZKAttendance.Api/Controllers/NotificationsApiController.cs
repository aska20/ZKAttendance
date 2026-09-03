using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>The bell menu. Notifications targeted at the current user or their role.</summary>
    [Route("api/Notifications")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Notifications")]
    [Authorize]
    public class NotificationsApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;

        public NotificationsApiController(AttendanceDbContext db) => _db = db;

        private int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        private string? Role => User.FindFirstValue(ClaimTypes.Role);

        private IQueryable<Domain.Entities.Notification> Mine()
            => _db.Notifications.Where(n => n.RecipientUserId == UserId || n.RecipientRole == Role);

        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get([FromQuery] bool unreadOnly = false, [FromQuery] int take = 30)
        {
            var q = Mine();
            if (unreadOnly) q = q.Where(n => !n.IsRead);

            var rows = await q
                .OrderByDescending(n => n.CreatedDate)
                .Take(Math.Clamp(take, 1, 100))
                .Select(n => new { n.NotificationId, n.Message, n.LinkPath, n.Type, n.IsRead, n.CreatedDate })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpGet("unread-count")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> UnreadCount()
            => Ok(new { count = await Mine().CountAsync(n => !n.IsRead) });

        [HttpPost("{id:long}/read")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> MarkRead(long id)
        {
            var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == id
                && (x.RecipientUserId == UserId || x.RecipientRole == Role));
            if (n is null) return NotFound();
            n.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok(new { id, read = true });
        }

        [HttpPost("read-all")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> MarkAllRead()
        {
            var updated = await Mine().Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
            return Ok(new { updated });
        }
    }
}
