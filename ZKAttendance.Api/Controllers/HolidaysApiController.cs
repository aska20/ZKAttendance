using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Holidays. A day marked here is excluded from the working-day count in
    /// the attendance overview and its cells show "Holiday" instead of "Absent".
    /// The weekly off (Saturday) is handled separately by the Nepali calendar
    /// and is not stored here.
    /// </summary>
    [Route("api/Holidays")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Holidays")]
    [Authorize(Roles = Roles.Management)]
    public class HolidaysApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<HolidaysApiController> _logger;

        public HolidaysApiController(AttendanceDbContext db, ILogger<HolidaysApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Holidays, optionally within a date range.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            var q = _db.Holidays.AsNoTracking().Where(h => h.IsActive);
            if (from is { } f) q = q.Where(h => h.HolidayDate >= f.Date);
            if (to is { } t) q = q.Where(h => h.HolidayDate <= t.Date);

            var rows = await q.OrderBy(h => h.HolidayDate)
                .Select(h => new { h.HolidayId, h.HolidayName, date = h.HolidayDate, h.HolidayType, h.Description })
                .ToListAsync();
            return Ok(rows);
        }

        /// <summary>Mark a day as a holiday. If one already exists on that date it is returned as-is.</summary>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Create([FromBody] HolidayRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.HolidayName))
                return BadRequest(ApiError.From("A name is required."));

            var date = request.Date.Date;
            var existing = await _db.Holidays.FirstOrDefaultAsync(h => h.IsActive && h.HolidayDate == date);
            if (existing is not null)
                return Ok(new { existing.HolidayId, existing.HolidayName, date = existing.HolidayDate });

            var holiday = new Holiday
            {
                HolidayName = request.HolidayName.Trim(),
                HolidayDate = date,
                Description = request.Description,
                HolidayType = request.HolidayType ?? "Public",
                DurationDays = 1,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            _db.Holidays.Add(holiday);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Holiday '{Name}' added on {Date} by {User}", holiday.HolidayName, date, User.Identity?.Name);
            return StatusCode(201, new { holiday.HolidayId, holiday.HolidayName, date = holiday.HolidayDate });
        }

        /// <summary>Remove a holiday (unmark the day).</summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(int id)
        {
            var row = await _db.Holidays.FirstOrDefaultAsync(h => h.HolidayId == id);
            if (row is null) return NotFound();
            _db.Holidays.Remove(row);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Remove whatever holiday sits on a given date. Convenience for the calendar toggle.</summary>
        [HttpDelete("on/{date}")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> DeleteOnDate(DateTime date)
        {
            var d = date.Date;
            var deleted = await _db.Holidays.Where(h => h.HolidayDate == d).ExecuteDeleteAsync();
            return Ok(new { date = d.ToString("yyyy-MM-dd"), deleted });
        }
    }

    public class HolidayRequest
    {
        public string HolidayName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? HolidayType { get; set; }
        public string? Description { get; set; }
    }
}
