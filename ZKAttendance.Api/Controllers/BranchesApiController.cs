using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>Branches — full CRUD. Thin wrapper over IBrancheService.</summary>
    [Route("api/Branches")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Branches")]
    [Authorize(Roles = Roles.Management)]
    public class BranchesApiController : ControllerBase
    {
        private readonly IBrancheService _service;
        private readonly ILogger<BranchesApiController> _logger;

        public BranchesApiController(IBrancheService service, ILogger<BranchesApiController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>All branches.</summary>
        /// <param name="withDevices">Include each branch's devices.</param>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAll([FromQuery] bool withDevices = false)
        {
            if (withDevices)
            {
                var full = await _service.GetAllBranchesWithDevicesAsync();
                return Ok(full.Select(b => new
                {
                    b.BranchId, b.BranchCode, b.BranchName, b.City, b.Address,
                    b.ContactPerson, b.ContactPhone, b.IsActive,
                    devices = b.Devices?.Select(d => new { d.DeviceId, d.DeviceName, d.DeviceIP, d.IsActive, d.IsOnline })
                }));
            }

            var items = await _service.GetAllBranchesAsync();
            return Ok(items.Select(Shape));
        }

        /// <summary>One branch by id.</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Get(int id)
        {
            var item = await _service.GetBranchByIdAsync(id);
            return item is null
                ? NotFound(ApiError.From($"Branch {id} not found"))
                : Ok(Shape(item));
        }

        /// <summary>Create a branch.</summary>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Create([FromBody] BranchRequest request)
        {
            try
            {
                var created = await _service.CreateBranchAsync(new Branch
                {
                    BranchCode = request.BranchCode,
                    BranchName = request.BranchName,
                    City = request.City,
                    Address = request.Address,
                    ContactPerson = request.ContactPerson,
                    ContactPhone = request.ContactPhone,
                    IsActive = request.IsActive,
                    CreatedDate = DateTime.Now
                });

                return CreatedAtAction(nameof(Get), new { id = created.BranchId }, Shape(created));
            }
            catch (InvalidOperationException ex)
            {
                // Duplicate branch code.
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Update a branch.</summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] BranchRequest request)
        {
            var existing = await _service.GetBranchByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Branch {id} not found"));

            existing.BranchCode = request.BranchCode;
            existing.BranchName = request.BranchName;
            existing.City = request.City;
            existing.Address = request.Address;
            existing.ContactPerson = request.ContactPerson;
            existing.ContactPhone = request.ContactPhone;
            existing.IsActive = request.IsActive;
            existing.ModifiedDate = DateTime.Now;

            try
            {
                return Ok(Shape(await _service.UpdateBranchAsync(existing)));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Delete a branch.</summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _service.GetBranchByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Branch {id} not found"));

            try
            {
                await _service.DeleteBranchAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting branch {BranchId}", id);
                return BadRequest(ApiError.From("Could not delete the branch. It may still have devices or employees."));
            }
        }

        private static object Shape(Branch b) => new
        {
            b.BranchId,
            b.BranchCode,
            b.BranchName,
            b.City,
            b.Address,
            b.ContactPerson,
            b.ContactPhone,
            b.IsActive,
            b.LastSyncTime
        };
    }
}
