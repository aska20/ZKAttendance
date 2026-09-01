using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>Departments — full CRUD.</summary>
    [Route("api/Departments")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Departments")]
    [Authorize(Roles = Roles.Management)]
    public class DepartmentsApiController : ControllerBase
    {
        private readonly IDepartmentService _service;
        private readonly ILogger<DepartmentsApiController> _logger;

        public DepartmentsApiController(IDepartmentService service, ILogger<DepartmentsApiController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>All departments.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllDepartmentsAsync();
            return Ok(items.Select(Shape));
        }

        /// <summary>One department by id.</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Get(int id)
        {
            var item = await _service.GetDepartmentByIdAsync(id);
            return item is null
                ? NotFound(ApiError.From($"Department {id} not found"))
                : Ok(Shape(item));
        }

        /// <summary>Create a department.</summary>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Create([FromBody] DepartmentRequest request)
        {
            try
            {
                var created = await _service.CreateDepartmentAsync(new Department
                {
                    DepartmentName = request.DepartmentName,
                    DepartmentCode = request.DepartmentCode,
                    ParentDepartmentId = request.ParentDepartmentId,
                    Description = request.Description,
                    IsActive = request.IsActive,
                    CreatedDate = DateTime.Now
                });

                return CreatedAtAction(nameof(Get), new { id = created.DepartmentId }, Shape(created));
            }
            catch (InvalidOperationException ex)
            {
                // The service throws this for a duplicate name.
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Update a department.</summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] DepartmentRequest request)
        {
            var existing = await _service.GetDepartmentByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Department {id} not found"));

            existing.DepartmentName = request.DepartmentName;
            existing.DepartmentCode = request.DepartmentCode;
            existing.ParentDepartmentId = request.ParentDepartmentId;
            existing.Description = request.Description;
            existing.IsActive = request.IsActive;
            existing.ModifiedDate = DateTime.Now;

            try
            {
                return Ok(Shape(await _service.UpdateDepartmentAsync(existing)));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Delete a department.</summary>
        /// <remarks>
        /// Refused if the department still has employees or sub-departments.
        /// Deleting it would orphan them, so the service checks first.
        /// </remarks>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _service.GetDepartmentByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Department {id} not found"));

            if (!await _service.CanDeleteDepartmentAsync(id))
                return BadRequest(ApiError.From(
                    "Cannot delete: the department still has employees or sub-departments."));

            await _service.DeleteDepartmentAsync(id);
            return NoContent();
        }

        private static object Shape(Department d) => new
        {
            d.DepartmentId,
            d.DepartmentName,
            d.DepartmentCode,
            d.ParentDepartmentId,
            d.Description,
            d.IsActive
        };
    }

    /// <summary>Employees — CRUD plus biometric device enrolment.</summary>
    [Route("api/Employees")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Employees")]
    [Authorize(Roles = Roles.Management)]
    public class EmployeesApiController : ControllerBase
    {
        private readonly IEmployeeService _employees;
        private readonly INepaliCalendar _nepali;
        private readonly ZKAttendance.Infrastructure.Persistence.AttendanceDbContext _db;
        private readonly ILogger<EmployeesApiController> _logger;

        public EmployeesApiController(
            IEmployeeService employees,
            INepaliCalendar nepali,
            ZKAttendance.Infrastructure.Persistence.AttendanceDbContext db,
            ILogger<EmployeesApiController> logger)
        {
            _employees = employees;
            _nepali = nepali;
            _db = db;
            _logger = logger;
        }

        /// <summary>All employees, optionally filtered by department.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAll([FromQuery] int? departmentId = null)
        {
            var items = departmentId.HasValue
                ? await _employees.GetEmployeesByDepartmentIdAsync(departmentId.Value)
                : await _employees.GetAllEmployeesAsync();

            return Ok(items.Select(Shape));
        }

        /// <summary>One employee by id.</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Get(int id)
        {
            var item = await _employees.GetEmployeeByIdAsync(id);
            return item is null
                ? NotFound(ApiError.From($"Employee {id} not found"))
                : Ok(Shape(item));
        }

        /// <summary>Create an employee.</summary>
        /// <remarks>
        /// Leave BiometricUserId empty and the next free number is allocated.
        /// Supplying one that is already taken is rejected — two people sharing
        /// an enrol number would make their punches indistinguishable.
        /// </remarks>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Create([FromBody] EmployeeRequest request)
        {
            var biometricId = string.IsNullOrWhiteSpace(request.BiometricUserId)
                ? await _employees.GetNextBiometricUserIdAsync()
                : request.BiometricUserId.Trim();

            if (await _employees.IsBiometricIdExistsAsync(biometricId))
                return BadRequest(ApiError.From($"Biometric ID '{biometricId}' is already in use"));

            var created = await _employees.CreateEmployeeAsync(new Employee
            {
                EmployeeName = request.EmployeeName,
                BiometricUserId = biometricId,
                DepartmentId = request.DepartmentId,
                DefaultShiftId = request.DefaultShiftId,
                PhoneNumber = request.PhoneNumber,
                Title = request.Title,
                Email = request.Email,
                SSN = request.SSN,
                Gender = request.Gender,
                BirthDate = request.BirthDate,
                HireDate = request.HireDate,
                CheckAttendance = request.CheckAttendance,
                CheckLate = request.CheckLate,
                CheckEarly = request.CheckEarly,
                CheckOvertime = request.CheckOvertime,
                CheckHoliday = request.CheckHoliday,
                IsActive = request.IsActive,
                CreatedDate = DateTime.Now
            });

            return CreatedAtAction(nameof(Get), new { id = created.EmployeeId }, Shape(created));
        }

        /// <summary>Update an employee.</summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] EmployeeRequest request)
        {
            var existing = await _employees.GetEmployeeByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Employee {id} not found"));

            if (!string.IsNullOrWhiteSpace(request.BiometricUserId) &&
                request.BiometricUserId != existing.BiometricUserId)
            {
                if (await _employees.IsBiometricIdExistsAsync(request.BiometricUserId, id))
                    return BadRequest(ApiError.From(
                        $"Biometric ID '{request.BiometricUserId}' is already in use"));

                existing.BiometricUserId = request.BiometricUserId.Trim();
            }

            existing.EmployeeName = request.EmployeeName;
            existing.DepartmentId = request.DepartmentId;
            existing.DefaultShiftId = request.DefaultShiftId;
            existing.PhoneNumber = request.PhoneNumber;
            existing.Title = request.Title;
            existing.Email = request.Email;
            existing.SSN = request.SSN;
            existing.Gender = request.Gender;
            existing.BirthDate = request.BirthDate;
            existing.HireDate = request.HireDate;
            existing.CheckAttendance = request.CheckAttendance;
            existing.CheckLate = request.CheckLate;
            existing.CheckEarly = request.CheckEarly;
            existing.CheckOvertime = request.CheckOvertime;
            existing.CheckHoliday = request.CheckHoliday;
            existing.IsActive = request.IsActive;
            existing.ModifiedDate = DateTime.Now;

            return Ok(Shape(await _employees.UpdateEmployeeAsync(existing)));
        }

        /// <summary>Deactivate an employee.</summary>
        /// <remarks>
        /// Sets IsActive to false rather than deleting. Their attendance history
        /// is a payroll record and must survive them leaving.
        /// </remarks>
        [HttpPost("{id:int}/deactivate")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Deactivate(int id)
        {
            var existing = await _employees.GetEmployeeByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Employee {id} not found"));

            existing.IsActive = false;
            existing.ModifiedDate = DateTime.Now;
            await _employees.UpdateEmployeeAsync(existing);

            _logger.LogInformation("Employee {Id} deactivated through the API", id);
            return Ok(new { id, isActive = false, message = "Employee deactivated" });
        }

        /// <summary>
        /// Permanently delete an employee. Allowed only when they have NO
        /// attendance history and NO device mappings — otherwise you would
        /// orphan payroll rows, so the API refuses and you should
        /// <c>deactivate</c> instead.
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _employees.GetEmployeeByIdAsync(id);
            if (existing is null)
                return NotFound(ApiError.From($"Employee {id} not found"));

            var punchCount = await _db.AttendanceLogs.CountAsync(a => a.EmployeeId == id);
            if (punchCount > 0)
                return BadRequest(ApiError.From(
                    $"Employee {id} has {punchCount} attendance record(s). Deactivate them instead of deleting, " +
                    "or purge their punches first from the Punches screen."));

            var maps = await _db.EmployeeDevices.Where(ed => ed.EmployeeId == id).ToListAsync();
            _db.EmployeeDevices.RemoveRange(maps);

            var accounts = await _db.ApiUsers.Where(u => u.EmployeeId == id).ToListAsync();
            foreach (var a in accounts) a.EmployeeId = null; // unlink, keep the login

            _db.Employees.Remove(await _db.Employees.FirstAsync(e => e.EmployeeId == id));
            await _db.SaveChangesAsync();

            _logger.LogWarning("Employee {Id} permanently deleted by {User}", id, User.Identity?.Name);
            return NoContent();
        }

        /// <summary>Link an employee to biometric devices.</summary>
        /// <remarks>
        /// Records which machines this person should exist on, and the enrol
        /// number each one knows them by. The numbers can differ per device:
        /// each machine allocates its own, so 1017 at head office may be 88 at
        /// the branch because 1017 was already taken there.
        ///
        /// This records intent. Writing the fingerprint template to the device
        /// is a separate step that needs the ZKTeco SDK.
        /// </remarks>
        [HttpPost("{id:int}/enroll-on-devices")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> EnrollOnDevices(
            int id,
            [FromBody] EnrollOnDevicesRequest request,
            [FromServices] IEmployeeDeviceEnrollment enrollment)
        {
            var employee = await _employees.GetEmployeeByIdAsync(id);
            if (employee is null)
                return NotFound(ApiError.From($"Employee {id} not found"));

            var result = await enrollment.AssignAsync(
                id, request.DeviceIds, request.DeviceUserIds);

            return Ok(new
            {
                employeeId = id,
                employee.EmployeeName,
                assigned = result.Select(r => new { r.DeviceId, r.DeviceUserId, r.IsEnrolled })
            });
        }

        /// <summary>Biometric IDs seen in attendance logs that belong to no employee.</summary>
        [HttpGet("unregistered")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetUnregistered()
            => Ok(await _employees.GetUnregisteredBiometricIdsAsync());

        private object Shape(Employee e) => new
        {
            e.EmployeeId,
            e.EmployeeName,
            e.BiometricUserId,
            e.DepartmentId,
            e.DefaultShiftId,
            e.PhoneNumber,
            e.Title,
            e.Email,
            e.SSN,
            e.Gender,
            e.BirthDate,
            e.CheckAttendance,
            e.CheckLate,
            e.CheckEarly,
            e.CheckOvertime,
            e.CheckHoliday,
            hireDate = e.HireDate,
            hireDateBs = e.HireDate.HasValue ? _nepali.ToBsString(e.HireDate.Value) : null,
            e.IsActive
        };
    }
}
