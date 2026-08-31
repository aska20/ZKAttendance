using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>Work shifts — full CRUD. Thin wrapper over IWorkShiftService.</summary>
    [Route("api/WorkShifts")]
    [ApiController]
    [Produces("application/json")]
    [Tags("WorkShifts")]
    [Authorize(Roles = Roles.Management)]
    public class WorkShiftsApiController : ControllerBase
    {
        private readonly IWorkShiftService _service;
        private readonly ILogger<WorkShiftsApiController> _logger;

        public WorkShiftsApiController(IWorkShiftService service, ILogger<WorkShiftsApiController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>All shifts.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAll()
            => Ok((await _service.GetAllShiftsAsync()).Select(Shape));

        /// <summary>One shift by id.</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Get(int id)
        {
            var item = await _service.GetShiftByIdAsync(id);
            return item is null
                ? NotFound(ApiError.From($"Shift {id} not found"))
                : Ok(Shape(item));
        }

        /// <summary>Create a shift.</summary>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Create([FromBody] WorkShiftRequest request)
        {
            try
            {
                var created = await _service.CreateShiftAsync(Map(new WorkShift(), request));
                return CreatedAtAction(nameof(Get), new { id = created.ShiftId }, Shape(created));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Update a shift.</summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] WorkShiftRequest request)
        {
            var existing = await _service.GetShiftByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Shift {id} not found"));

            Map(existing, request);
            existing.ModifiedDate = DateTime.Now;

            try
            {
                return Ok(Shape(await _service.UpdateShiftAsync(existing)));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Delete a shift. Refused if employees are still assigned to it.</summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _service.GetShiftByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Shift {id} not found"));

            if (!await _service.CanDeleteShiftAsync(id))
                return BadRequest(ApiError.From("Cannot delete: employees are still assigned to this shift."));

            try
            {
                await _service.DeleteShiftAsync(id);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        private static WorkShift Map(WorkShift s, WorkShiftRequest r)
        {
            s.ShiftName = r.ShiftName;
            s.Description = r.Description;
            s.StartTime = r.StartTime;
            s.EndTime = r.EndTime;
            s.LateMinutes = r.LateMinutes;
            s.EarlyMinutes = r.EarlyMinutes;
            s.BreakMinutes = r.BreakMinutes;
            s.IsBreakPaid = r.IsBreakPaid;
            s.WorkMinutes = r.WorkMinutes;
            s.RequireCheckIn = r.RequireCheckIn;
            s.RequireCheckOut = r.RequireCheckOut;
            s.IsOvernight = r.IsOvernight;
            s.OvertimeStartMinutes = r.OvertimeStartMinutes;
            s.MinHoursForFullDay = r.MinHoursForFullDay;
            s.MaxRegularHours = r.MaxRegularHours;
            s.RoundingMinutes = r.RoundingMinutes;
            s.WorkDays = r.WorkDays;
            s.IsActive = r.IsActive;
            return s;
        }

        private static object Shape(WorkShift s) => new
        {
            s.ShiftId,
            s.ShiftName,
            s.Description,
            startTime = s.StartTime.ToString(@"hh\:mm"),
            endTime = s.EndTime.ToString(@"hh\:mm"),
            s.LateMinutes,
            s.EarlyMinutes,
            s.BreakMinutes,
            s.IsBreakPaid,
            s.WorkMinutes,
            s.RequireCheckIn,
            s.RequireCheckOut,
            s.IsOvernight,
            s.OvertimeStartMinutes,
            s.MinHoursForFullDay,
            s.MaxRegularHours,
            s.RoundingMinutes,
            s.WorkDays,
            s.IsActive
        };
    }
}
