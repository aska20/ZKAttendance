using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Dtos;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Services.Common;

namespace ZKAttendance.Web.Controllers
{
    [Authorize]
    public class EmployeesController : Controller
    {
        private readonly IEmployeeService _employeeService;
        private readonly LookupService _lookupService;
        private readonly AttendanceDbContext _context;
        private readonly ILogger<EmployeesController> _logger;
        public EmployeesController(
    IEmployeeService employeeService,
    LookupService lookupService,
    AttendanceDbContext context,
    ILogger<EmployeesController> logger)
        {
            _employeeService = employeeService;
            _lookupService = lookupService;
            _context = context;
            _logger = logger;
        }

        // GET: Employees
        public async Task<IActionResult> Index()
        {
            try
            {
                var employees = await _employeeService.GetAllEmployeesAsync();
                return View(employees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing employees");
                TempData["Error"] = "An error occurred while loading the employee list";
                return View(new List<Employee>());
            }
        }

        // GET: Employees/Create
        public async Task<IActionResult> Create()
        {
            try
            {
                var nextBiometricId = await _employeeService.GetNextBiometricUserIdAsync();
                var lastBiometricIds = await _employeeService.GetLastBiometricUserIdsAsync(10);

                var viewModel = new EmployeeFormViewModel
                {
                    BiometricUserId = nextBiometricId,
                    IsActive = true,
                    CheckAttendance = true,
                    CheckLate = true,
                    CheckEarly = true,
                    CheckOvertime = true,
                    CheckHoliday = true
                };

                ViewBag.NextBiometricId = nextBiometricId;
                ViewBag.LastBiometricIds = lastBiometricIds;

                await PopulateDropdowns(viewModel);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening the add-employee page");
                TempData["Error"] = "An error occurred while opening the page";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Employees/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EmployeeFormViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // 1. Create the employee
                    var employee = new Employee
                    {
                        BiometricUserId = viewModel.BiometricUserId,
                        EmployeeName = viewModel.EmployeeName,
                        SSN = viewModel.SSN,
                        Gender = viewModel.Gender,
                        Title = viewModel.Title,
                        PhoneNumber = viewModel.PhoneNumber,
                        BirthDate = viewModel.BirthDate,
                        HireDate = viewModel.HireDate,
                        DepartmentId = viewModel.DepartmentId,
                        DefaultShiftId = viewModel.DefaultShiftId,
                        CheckAttendance = viewModel.CheckAttendance,
                        CheckLate = viewModel.CheckLate,
                        CheckEarly = viewModel.CheckEarly,
                        CheckOvertime = viewModel.CheckOvertime,
                        CheckHoliday = viewModel.CheckHoliday,
                        IsActive = viewModel.IsActive
                    };

                    await _employeeService.CreateEmployeeAsync(employee);

                    // 2. Link the employee to the selected devices
                    await SaveEmployeeDevices(employee.EmployeeId, viewModel.SelectedDeviceIds);

                    // 3. Link the employee to branches, if any
                    if (viewModel.SelectedBranchIds?.Any() == true)
                    {
                        await SaveEmployeeBranches(employee.EmployeeId, viewModel.SelectedBranchIds);
                    }

                    TempData["Success"] = "Employee created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("BiometricUserId", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating employee");
                    ModelState.AddModelError("", "An error occurred while creating the employee");
                }
            }

            // Reload the biometric ViewBag after a validation error
            ViewBag.NextBiometricId = viewModel.BiometricUserId;
            ViewBag.LastBiometricIds = await _employeeService.GetLastBiometricUserIdsAsync(10);

            await PopulateDropdowns(viewModel);
            return View(viewModel);
        }

        // GET: Employees/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                // Simplified, null-safe query
                var employee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == id);

                if (employee == null)
                {
                    TempData["Error"] = "Employee not found";
                    return RedirectToAction(nameof(Index));
                }

                // Load related data separately to avoid nulls
                var selectedBranches = await _context.EmployeeBranches
                    .Where(eb => eb.EmployeeId == id && eb.IsActive)
                    .Select(eb => eb.BranchId)
                    .ToListAsync();

                var selectedDevices = await _context.EmployeeDevices
                    .Where(ed => ed.EmployeeId == id && ed.IsActive)
                    .Select(ed => ed.DeviceId)
                    .ToListAsync();

                var viewModel = new EmployeeFormViewModel
                {
                    EmployeeId = employee.EmployeeId,
                    BiometricUserId = employee.BiometricUserId,
                    EmployeeName = employee.EmployeeName,
                    SSN = employee.SSN,
                    Gender = employee.Gender,
                    Title = employee.Title,
                    PhoneNumber = employee.PhoneNumber,
                    BirthDate = employee.BirthDate,
                    HireDate = employee.HireDate,
                    DepartmentId = employee.DepartmentId,
                    DefaultShiftId = employee.DefaultShiftId,
                    CheckAttendance = employee.CheckAttendance,
                    CheckLate = employee.CheckLate,
                    CheckEarly = employee.CheckEarly,
                    CheckOvertime = employee.CheckOvertime,
                    CheckHoliday = employee.CheckHoliday,
                    IsActive = employee.IsActive,
                    SelectedBranchIds = selectedBranches,
                    SelectedDeviceIds = selectedDevices
                };

                await PopulateDropdowns(viewModel);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading employee data {EmployeeId}. Error: {Message}", id, ex.Message);
                TempData["Error"] = $"An error occurred while loading employee data: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Employees/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EmployeeFormViewModel viewModel)
        {
            if (id != viewModel.EmployeeId)
            {
                TempData["Error"] = "Invalid data";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var employee = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == id);

                    if (employee == null)
                    {
                        TempData["Error"] = "Employee not found";
                        return RedirectToAction(nameof(Index));
                    }

                    // Update the employee record
                    employee.BiometricUserId = viewModel.BiometricUserId;
                    employee.EmployeeName = viewModel.EmployeeName;
                    employee.SSN = viewModel.SSN;
                    employee.Gender = viewModel.Gender;
                    employee.Title = viewModel.Title;
                    employee.PhoneNumber = viewModel.PhoneNumber;
                    employee.BirthDate = viewModel.BirthDate;
                    employee.HireDate = viewModel.HireDate;
                    employee.DepartmentId = viewModel.DepartmentId;
                    employee.DefaultShiftId = viewModel.DefaultShiftId;
                    employee.CheckAttendance = viewModel.CheckAttendance;
                    employee.CheckLate = viewModel.CheckLate;
                    employee.CheckEarly = viewModel.CheckEarly;
                    employee.CheckOvertime = viewModel.CheckOvertime;
                    employee.CheckHoliday = viewModel.CheckHoliday;
                    employee.IsActive = viewModel.IsActive;
                    employee.ModifiedDate = DateTime.Now;

                    await _context.SaveChangesAsync();

                    // Update device links
                    await SaveEmployeeDevices(employee.EmployeeId, viewModel.SelectedDeviceIds);

                    // Update branch links, if any
                    if (viewModel.SelectedBranchIds?.Any() == true)
                    {
                        await SaveEmployeeBranches(employee.EmployeeId, viewModel.SelectedBranchIds);
                    }

                    TempData["Success"] = "Employee updated successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("BiometricUserId", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating employee {EmployeeId}", id);
                    ModelState.AddModelError("", "An error occurred while updating the employee");
                }
            }

            await PopulateDropdowns(viewModel);
            return View(viewModel);
        }

        // GET: Employees/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var employee = await _context.Employees
                    .Include(e => e.Department)
                    .Include(e => e.DefaultShift)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == id);

                if (employee == null)
                {
                    TempData["Error"] = "Employee not found";
                    return RedirectToAction(nameof(Index));
                }

                // Load devices and branches separately to avoid nulls
                var assignedDevices = await _context.EmployeeDevices
                    .Where(ed => ed.EmployeeId == id && ed.IsActive)
                    .Include(ed => ed.Device)
                        .ThenInclude(d => d!.Branch)
                    .Where(ed => ed.Device != null && ed.Device.IsActive && ed.Device.Branch != null)
                    .ToListAsync();

                var viewModel = new EmployeeDetailsViewModel
                {
                    Employee = employee,
                    AssignedBranches = assignedDevices
                        .GroupBy(ed => ed.Device!.Branch!)
                        .Select(g => new EmployeeDetailsViewModel.BranchWithDevicesViewModel
                        {
                            BranchId = g.Key.BranchId,
                            BranchCode = g.Key.BranchCode,
                            BranchName = g.Key.BranchName,
                            City = g.Key.City,
                            Devices = g.Select(ed => new EmployeeDetailsViewModel.DeviceInfoViewModel
                            {
                                DeviceId = ed.Device!.DeviceId,
                                DeviceName = ed.Device.DeviceName,
                                DeviceIP = ed.Device.DeviceIP,
                                SerialNumber = ed.Device.SerialNumber
                            }).ToList()
                        })
                        .OrderBy(b => b.City)
                        .ThenBy(b => b.BranchName)
                        .ToList()
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing employee details {EmployeeId}. Full error: {Message}\n{StackTrace}",
                    id, ex.Message, ex.StackTrace);
                TempData["Error"] = $"An error occurred while loading details: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Employees/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _employeeService.DeleteEmployeeAsync(id);
                TempData["Success"] = "Employee deleted successfully";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee {EmployeeId}", id);
                TempData["Error"] = "An error occurred while deleting the employee";
            }
            return RedirectToAction(nameof(Index));
        }

        // ═════════════════════════════════════════════════════════════
        // ✅ Helper Methods
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Save or update the employee-to-device links
        /// </summary>
        private async Task SaveEmployeeDevices(int employeeId, List<int>? selectedDeviceIds)
        {
            // Remember the biometric ID already recorded for each device, so
            // re-saving the form does not wipe an ID that differs from the
            // employee's default. Without this, an employee who is 1017 on the
            // head office device and 88 on the branch device would silently
            // lose the 88 every time someone edited their record.
            var existingDevices = await _context.EmployeeDevices
                .Where(ed => ed.EmployeeId == employeeId)
                .ToListAsync();

            var previousIds = existingDevices
                .ToDictionary(ed => ed.DeviceId, ed => ed.BiometricUserId);

            _context.EmployeeDevices.RemoveRange(existingDevices);

            // Add the new device links
            if (selectedDeviceIds?.Any() == true)
            {
                // Fall back to the employee's default biometric ID for a device
                // they have not been enrolled on before. An administrator can
                // correct it afterwards if that device assigned a different one.
                var employee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

                var defaultBiometricId = employee?.BiometricUserId ?? string.Empty;

                foreach (var deviceId in selectedDeviceIds)
                {
                    var biometricId = previousIds.TryGetValue(deviceId, out var kept)
                                      && !string.IsNullOrWhiteSpace(kept)
                        ? kept
                        : defaultBiometricId;

                    _context.EmployeeDevices.Add(new EmployeeDevice
                    {
                        EmployeeId = employeeId,
                        DeviceId = deviceId,
                        BiometricUserId = biometricId,
                        IsEnrolled = previousIds.ContainsKey(deviceId),
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Save or update the employee-to-branch links
        /// </summary>
        private async Task SaveEmployeeBranches(int employeeId, List<int>? selectedBranchIds)
        {
            // Remove the old branch links
            var existingBranches = await _context.EmployeeBranches
                .Where(eb => eb.EmployeeId == employeeId)
                .ToListAsync();

            _context.EmployeeBranches.RemoveRange(existingBranches);

            // Add the new branch links
            if (selectedBranchIds?.Any() == true)
            {
                foreach (var branchId in selectedBranchIds)
                {
                    _context.EmployeeBranches.Add(new EmployeeBranch
                    {
                        EmployeeId = employeeId,
                        BranchId = branchId,
                        AssignedDate = DateTime.Now,
                        IsActive = true
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Populate dropdowns plus the branch and device list
        /// </summary>
        private async Task PopulateDropdowns(EmployeeFormViewModel viewModel)
        {
            // ✅ Departments & Shifts
            ViewBag.Departments = new SelectList(
                await _lookupService.GetActiveDepartmentsAsync(),
                "DepartmentId",
                "DepartmentName",
                viewModel.DepartmentId
            );

            ViewBag.Shifts = new SelectList(
                await _lookupService.GetActiveShiftsAsync(),
                "ShiftId",
                "ShiftName",
                viewModel.DefaultShiftId
            );

            // Branches and devices grouped by city
            var branches = await _context.Branches
                .Include(b => b.Devices.Where(d => d.IsActive))
                .Where(b => b.IsActive)
                .OrderBy(b => b.City)
                .ThenBy(b => b.BranchName)
                .ToListAsync();

            viewModel.BranchesByCity = branches
                .GroupBy(b => b.City ?? "Not set")
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(b => new BranchDeviceViewModel
                    {
                        BranchId = b.BranchId,
                        BranchCode = b.BranchCode,
                        BranchName = b.BranchName,
                        City = b.City,
                        IsBranchSelected = viewModel.SelectedBranchIds?.Contains(b.BranchId) ?? false,
                        Devices = b.Devices.Select(d => new DeviceSelectionViewModel
                        {
                            DeviceId = d.DeviceId,
                            DeviceName = d.DeviceName,
                            DeviceIP = d.DeviceIP,
                            SerialNumber = d.SerialNumber,
                            IsSelected = viewModel.SelectedDeviceIds?.Contains(d.DeviceId) ?? false
                        }).ToList()
                    }).ToList()
                );
        }

        // GET: Employees/UnregisteredBiometricIds
        public async Task<IActionResult> UnregisteredBiometricIds()
        {
            try
            {
                var unregisteredIds = await _employeeService.GetUnregisteredBiometricIdsAsync();

                ViewBag.Message = unregisteredIds.Any()
                    ? $"Found {unregisteredIds.Count} unregistered biometric ID"
                    : "All biometric IDs are registered";

                return View(unregisteredIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing unregistered biometric IDs");
                TempData["Error"] = "An error occurred while loading the list";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Employees/CreateFromBiometricId
        public async Task<IActionResult> CreateFromBiometricId(string biometricUserId)
        {
            if (string.IsNullOrEmpty(biometricUserId))
            {
                TempData["Error"] = "Invalid biometric ID";
                return RedirectToAction(nameof(UnregisteredBiometricIds));
            }

            try
            {
                var nextBiometricId = await _employeeService.GetNextBiometricUserIdAsync();
                var lastBiometricIds = await _employeeService.GetLastBiometricUserIdsAsync(10);

                var viewModel = new EmployeeFormViewModel
                {
                    BiometricUserId = biometricUserId,
                    IsActive = true,
                    CheckAttendance = true,
                    CheckLate = true,
                    CheckEarly = true,
                    CheckOvertime = true,
                    CheckHoliday = true
                };

                ViewBag.NextBiometricId = nextBiometricId;
                ViewBag.LastBiometricIds = lastBiometricIds;
                ViewBag.FromUnregistered = true;

                await PopulateDropdowns(viewModel);
                return View("Create", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening the add-employee page from a biometric ID {BiometricUserId}", biometricUserId);
                TempData["Error"] = "An error occurred while opening the page";
                return RedirectToAction(nameof(UnregisteredBiometricIds));
            }
        }
    }
}
    