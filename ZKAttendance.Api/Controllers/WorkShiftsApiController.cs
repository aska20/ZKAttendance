using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    public class WorkShiftRequest
    {
        public string ShiftName { get; set; } = string.Empty;
        /// <summary>"HH:mm", e.g. "10:00".</summary>
        public string StartTime { get; set; } = "10:00";
        public string EndTime { get; set; } = "18:00";
        public int LateMinutes { get; set; } = 15;
        public int EarlyMinutes { get; set; } = 15;
        public int BreakMinutes { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class AssignShiftRequest
    {
        public List<int> EmployeeIds { get; set; } = new();
        public int? ShiftId { get; set; }
    }

    /// <summary>
    /// Work shifts, and who is on them.
    ///
    /// The table and entity already existed and the attendance grid already
    /// read <c>EndTime</c> to decide when a day closes, but there was no way to
    /// create a shift or put anybody on one. This is that missing half.
    ///
    /// An employee's <c>DefaultShiftId</c> is what the grid honours. Leaving it
    /// null is legitimate: those people fall back to the office hours set on
    /// the Settings screen.
    /// </summary>
    [Route("api/WorkShifts")]
    [ApiController]
    [Produces("application/json")]
    [Tags("WorkShifts")]
    [Authorize(Roles = Roles.Management)]
    public class WorkShiftsApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly ILogger<WorkShiftsApiController> _logger;

        public WorkShiftsApiController(AttendanceDbContext db, ILogger<WorkShiftsApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>All shifts, with how many employees are on each.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        {
            var query = _db.WorkShifts.AsQueryable();
            if (!includeInactive) query = query.Where(s => s.IsActive);

            var shifts = await query.OrderBy(s => s.StartTime).ToListAsync(ct);

            var counts = await _db.Employees
                .Where(e => e.IsActive && e.DefaultShiftId != null)
                .GroupBy(e => e.DefaultShiftId!.Value)
                .Select(g => new { ShiftId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ShiftId, x => x.Count, ct);

            var unassigned = await _db.Employees
                .CountAsync(e => e.IsActive && e.DefaultShiftId == null, ct);

            return Ok(new
            {
                unassignedEmployees = unassigned,
                shifts = shifts.Select(s => Shape(s, counts.GetValueOrDefault(s.ShiftId)))
            });
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var shift = await _db.WorkShifts.FirstOrDefaultAsync(s => s.ShiftId == id, ct);
            if (shift is null) return NotFound(ApiError.From("That shift no longer exists."));

            var employees = await _db.Employees
                .Where(e => e.DefaultShiftId == id && e.IsActive)
                .OrderBy(e => e.EmployeeName)
                .Select(e => new { e.EmployeeId, e.EmployeeName, e.BiometricUserId })
                .ToListAsync(ct);

            return Ok(new { shift = Shape(shift, employees.Count), employees });
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Create([FromBody] WorkShiftRequest request, CancellationToken ct)
        {
            var parsed = Validate(request, out var error);
            if (error is not null) return BadRequest(ApiError.From(error));

            if (await _db.WorkShifts.AnyAsync(s => s.ShiftName == request.ShiftName.Trim(), ct))
                return BadRequest(ApiError.From($"A shift called '{request.ShiftName.Trim()}' already exists."));

            var shift = new WorkShift
            {
                ShiftName = request.ShiftName.Trim(),
                StartTime = parsed.start,
                EndTime = parsed.end,
                LateMinutes = request.LateMinutes,
                EarlyMinutes = request.EarlyMinutes,
                BreakMinutes = request.BreakMinutes,
                WorkMinutes = parsed.workMinutes,
                Description = request.Description?.Trim(),
                IsActive = request.IsActive,
                CreatedDate = DateTime.Now
            };

            _db.WorkShifts.Add(shift);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Shift '{Name}' created by {User}", shift.ShiftName, User.Identity?.Name);
            return CreatedAtAction(nameof(Get), new { id = shift.ShiftId }, Shape(shift, 0));
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] WorkShiftRequest request, CancellationToken ct)
        {
            var shift = await _db.WorkShifts.FirstOrDefaultAsync(s => s.ShiftId == id, ct);
            if (shift is null) return NotFound(ApiError.From("That shift no longer exists."));

            var parsed = Validate(request, out var error);
            if (error is not null) return BadRequest(ApiError.From(error));

            if (await _db.WorkShifts.AnyAsync(s => s.ShiftName == request.ShiftName.Trim() && s.ShiftId != id, ct))
                return BadRequest(ApiError.From($"Another shift is already called '{request.ShiftName.Trim()}'."));

            shift.ShiftName = request.ShiftName.Trim();
            shift.StartTime = parsed.start;
            shift.EndTime = parsed.end;
            shift.LateMinutes = request.LateMinutes;
            shift.EarlyMinutes = request.EarlyMinutes;
            shift.BreakMinutes = request.BreakMinutes;
            shift.WorkMinutes = parsed.workMinutes;
            shift.Description = request.Description?.Trim();
            shift.IsActive = request.IsActive;
            shift.ModifiedDate = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            // Changing EndTime moves when a day closes, which changes who counts
            // as absent. Worth a log line when someone asks why numbers moved.
            _logger.LogInformation(
                "Shift '{Name}' updated by {User}: {Start} to {End}",
                shift.ShiftName, User.Identity?.Name, shift.StartTime, shift.EndTime);

            return Ok(Shape(shift, await _db.Employees.CountAsync(e => e.DefaultShiftId == id && e.IsActive, ct)));
        }

        /// <summary>
        /// Deactivates rather than deletes when anyone is still on the shift.
        /// Hard-deleting would null their DefaultShiftId silently and quietly
        /// move them onto the default office hours.
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var shift = await _db.WorkShifts.FirstOrDefaultAsync(s => s.ShiftId == id, ct);
            if (shift is null) return NotFound(ApiError.From("That shift no longer exists."));

            var onShift = await _db.Employees.CountAsync(e => e.DefaultShiftId == id, ct);

            if (onShift > 0)
            {
                shift.IsActive = false;
                shift.ModifiedDate = DateTime.Now;
                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    deactivated = true,
                    message = $"{onShift} employee(s) are still on this shift, so it was deactivated rather than deleted. Move them to another shift first if you want it gone."
                });
            }

            _db.WorkShifts.Remove(shift);
            await _db.SaveChangesAsync(ct);
            return Ok(new { deleted = true, message = "Shift deleted." });
        }

        /// <summary>
        /// Put employees on a shift, or take them off it by passing a null
        /// shiftId, in which case they fall back to the office hours in Settings.
        /// </summary>
        [HttpPost("assign")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Assign([FromBody] AssignShiftRequest request, CancellationToken ct)
        {
            if (request.EmployeeIds.Count == 0)
                return BadRequest(ApiError.From("Select at least one employee."));

            if (request.ShiftId.HasValue &&
                !await _db.WorkShifts.AnyAsync(s => s.ShiftId == request.ShiftId.Value, ct))
                return BadRequest(ApiError.From("That shift no longer exists."));

            var employees = await _db.Employees
                .Where(e => request.EmployeeIds.Contains(e.EmployeeId))
                .ToListAsync(ct);

            foreach (var e in employees)
            {
                e.DefaultShiftId = request.ShiftId;
                e.ModifiedDate = DateTime.Now;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "{Count} employee(s) moved to shift {ShiftId} by {User}",
                employees.Count, request.ShiftId?.ToString() ?? "none", User.Identity?.Name);

            return Ok(new
            {
                updated = employees.Count,
                message = request.ShiftId.HasValue
                    ? $"{employees.Count} employee(s) assigned."
                    : $"{employees.Count} employee(s) removed from their shift. They now follow the office hours in Settings."
            });
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static (TimeSpan start, TimeSpan end, int workMinutes) Validate(
            WorkShiftRequest r, out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(r.ShiftName))
            {
                error = "A shift name is required.";
                return default;
            }

            if (!TimeSpan.TryParse(r.StartTime, out var start))
            {
                error = "Start time must look like 10:00.";
                return default;
            }

            if (!TimeSpan.TryParse(r.EndTime, out var end))
            {
                error = "End time must look like 18:00.";
                return default;
            }

            if (start == end)
            {
                error = "Start and end time cannot be the same.";
                return default;
            }

            // A night shift legitimately ends before it starts, so wrap rather
            // than reject. 22:00 to 06:00 is eight hours, not minus sixteen.
            var span = end > start ? end - start : end.Add(TimeSpan.FromHours(24)) - start;
            var workMinutes = (int)span.TotalMinutes - Math.Max(0, r.BreakMinutes);

            if (workMinutes <= 0)
            {
                error = "The break is longer than the shift.";
                return default;
            }

            if (r.LateMinutes is < 0 or > 240)
            {
                error = "Late grace must be between 0 and 240 minutes.";
                return default;
            }

            return (start, end, workMinutes);
        }

        private static object Shape(WorkShift s, int employeeCount) => new
        {
            s.ShiftId,
            s.ShiftName,
            startTime = s.StartTime.ToString(@"hh\:mm"),
            endTime = s.EndTime.ToString(@"hh\:mm"),
            s.LateMinutes,
            s.EarlyMinutes,
            s.BreakMinutes,
            s.WorkMinutes,
            workHours = Math.Round(s.WorkMinutes / 60.0, 2),
            // True when the shift runs past midnight.
            isOvernight = s.EndTime <= s.StartTime,
            s.Description,
            s.IsActive,
            employeeCount
        };
    }
}
