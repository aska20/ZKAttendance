using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Api.Services;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Security;

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
        private readonly Notifier _notifier;
        private readonly ILogger<EmployeesApiController> _logger;

        public EmployeesApiController(
            IEmployeeService employees,
            INepaliCalendar nepali,
            ZKAttendance.Infrastructure.Persistence.AttendanceDbContext db,
            Notifier notifier,
            ILogger<EmployeesApiController> logger)
        {
            _employees = employees;
            _nepali = nepali;
            _db = db;
            _notifier = notifier;
            _logger = logger;
        }

        private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        private bool IsAdmin => User.IsInRole("Admin");

        /// <summary>All approved employees, optionally filtered by department.</summary>
        /// <param name="includePending">Also return employees still awaiting approval.</param>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAll([FromQuery] int? departmentId = null, [FromQuery] bool includePending = false)
        {
            var items = departmentId.HasValue
                ? await _employees.GetEmployeesByDepartmentIdAsync(departmentId.Value)
                : await _employees.GetAllEmployeesAsync();

            // Hide records awaiting / denied approval. Legacy rows have an
            // empty ApprovalStatus and count as approved.
            if (!includePending)
                items = items.Where(e => e.ApprovalStatus != "Pending" && e.ApprovalStatus != "Rejected").ToList();

            var loginEmpIds = await _db.ApiUsers
                .Where(u => u.EmployeeId != null)
                .Select(u => u.EmployeeId!.Value)
                .ToListAsync();

            return Ok(items.Select(e => Shape(e, loginEmpIds.Contains(e.EmployeeId))));
        }

        /// <summary>Employees added by HR that still need an Admin decision.</summary>
        [HttpGet("pending")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Pending()
        {
            var rows = await _db.Employees
                .Where(e => e.ApprovalStatus == "Pending")
                .OrderBy(e => e.CreatedDate)
                .ToListAsync();

            var requesters = await _db.ApiUsers
                .Where(u => rows.Select(r => r.RequestedByUserId).Contains(u.ApiUserId))
                .ToDictionaryAsync(u => u.ApiUserId, u => u.Username);

            return Ok(rows.Select(e => new
            {
                e.EmployeeId,
                e.EmployeeName,
                e.BiometricUserId,
                e.DepartmentId,
                e.Title,
                e.CreatedDate,
                requestedBy = e.RequestedByUserId.HasValue && requesters.TryGetValue(e.RequestedByUserId.Value, out var u) ? u : null
            }));
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

            // Admin → approved immediately. HR → pending an Admin decision.
            var pending = !IsAdmin;

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
                IsActive = pending ? false : request.IsActive,
                ApprovalStatus = pending ? "Pending" : "Approved",
                RequestedByUserId = CurrentUserId,
                ApprovedByUserId = pending ? null : CurrentUserId,
                ApprovedDate = pending ? null : DateTime.Now,
                CreatedDate = DateTime.Now
            });

            if (pending)
            {
                // CreateEmployeeAsync force-sets IsActive = true; a pending
                // record must stay inactive until an admin approves it.
                created.IsActive = false;
                await _db.SaveChangesAsync();

                await _notifier.ToRoleAsync("Admin",
                    $"{User.Identity?.Name} requested to add employee \"{created.EmployeeName}\".",
                    "/employees/pending", "employee-approval-request");
                if (CurrentUserId is { } uid)
                    await _notifier.ToUserAsync(uid,
                        $"Your request to add \"{created.EmployeeName}\" was sent to an admin for approval.",
                        "/employees", "employee-approval-request");
            }

            return CreatedAtAction(nameof(Get), new { id = created.EmployeeId }, Shape(created, false));
        }

        /// <summary>Approve a pending employee. Admin only.</summary>
        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Approve(int id)
        {
            var e = await _db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id);
            if (e is null) return NotFound(ApiError.From($"Employee {id} not found"));
            if (e.ApprovalStatus != "Pending") return BadRequest(ApiError.From("This employee is not pending approval."));

            e.ApprovalStatus = "Approved";
            e.IsActive = true;
            e.ApprovedByUserId = CurrentUserId;
            e.ApprovedDate = DateTime.Now;
            await _db.SaveChangesAsync();

            if (e.RequestedByUserId is { } uid)
                await _notifier.ToUserAsync(uid,
                    $"\"{e.EmployeeName}\" was approved and added by {User.Identity?.Name}.",
                    "/employees", "employee-approved");

            return Ok(new { id, e.ApprovalStatus, message = "Employee approved" });
        }

        /// <summary>Reject a pending employee. Admin only.</summary>
        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Reject(int id, [FromBody] RejectRequest? body = null)
        {
            var e = await _db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id);
            if (e is null) return NotFound(ApiError.From($"Employee {id} not found"));
            if (e.ApprovalStatus != "Pending") return BadRequest(ApiError.From("This employee is not pending approval."));

            e.ApprovalStatus = "Rejected";
            e.IsActive = false;
            e.ApprovedByUserId = CurrentUserId;
            e.ApprovedDate = DateTime.Now;
            await _db.SaveChangesAsync();

            if (e.RequestedByUserId is { } uid)
                await _notifier.ToUserAsync(uid,
                    $"Your request to add \"{e.EmployeeName}\" was rejected by {User.Identity?.Name}." +
                    (string.IsNullOrWhiteSpace(body?.Reason) ? "" : $" Reason: {body!.Reason}"),
                    "/employees", "employee-rejected");

            return Ok(new { id, e.ApprovalStatus, message = "Employee rejected" });
        }

        /// <summary>Give an existing employee a login account (role Employee, linked to them).</summary>
        [HttpPost("{id:int}/create-login")]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(ApiError), 400)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> CreateLogin(int id, [FromBody] CreateLoginRequest request)
        {
            var emp = await _employees.GetEmployeeByIdAsync(id);
            if (emp is null) return NotFound(ApiError.From($"Employee {id} not found"));

            var username = request.Username.Trim();
            if (username.Length < 3) return BadRequest(ApiError.From("Username must be at least 3 characters."));
            if (request.Password.Length < 8) return BadRequest(ApiError.From("Password must be at least 8 characters."));

            if (await _db.ApiUsers.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
                return BadRequest(ApiError.From($"Username '{username}' is already taken."));
            if (await _db.ApiUsers.AnyAsync(u => u.EmployeeId == id))
                return BadRequest(ApiError.From("This employee already has a login."));

            var (hash, salt) = PasswordHasher.HashPassword(request.Password);
            var user = new ApiUser
            {
                Username = username,
                Email = request.Email?.Trim() ?? emp.Email ?? $"{username}@zkattendance.local",
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = "Employee",
                EmployeeId = id,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            _db.ApiUsers.Add(user);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Login '{Username}' created for employee {Id} by {By}", username, id, User.Identity?.Name);
            return StatusCode(201, new { user.ApiUserId, user.Username, user.Role, employeeId = id });
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

        private object Shape(Employee e, bool hasLogin = false) => new
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
            e.IsActive,
            e.ApprovalStatus,
            hasLogin
        };
    }

    public class RejectRequest
    {
        public string? Reason { get; set; }
    }

    public class CreateLoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
