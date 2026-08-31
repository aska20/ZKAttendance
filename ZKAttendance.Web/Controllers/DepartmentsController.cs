using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Web.Controllers
{
    [Authorize(Roles = "Admin,HR")]
    public class DepartmentsController : Controller
    {
        private readonly IDepartmentService _departmentService;
        private readonly ILogger<DepartmentsController> _logger;

        public DepartmentsController(
            IDepartmentService departmentService,
            ILogger<DepartmentsController> logger)
        {
            _departmentService = departmentService;
            _logger = logger;
        }

        // GET: Departments
        public async Task<IActionResult> Index()
        {
            try
            {
                var departments = await _departmentService.GetAllDepartmentsAsync();
                return View(departments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing departments");
                TempData["Error"] = "An error occurred while loading the department list";
                return View(new List<Department>());
            }
        }

        // GET: Departments/Create
        public async Task<IActionResult> Create()
        {
            await PopulateParentDepartments();
            return View();
        }

        // POST: Departments/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Department department)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _departmentService.CreateDepartmentAsync(department);
                    TempData["Success"] = "Department created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating department");
                    ModelState.AddModelError("", "An error occurred while creating the department");
                }
            }

            await PopulateParentDepartments(department.ParentDepartmentId);
            return View(department);
        }

        // GET: Departments/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var department = await _departmentService.GetDepartmentByIdAsync(id);
                if (department == null)
                {
                    TempData["Error"] = "Department not found";
                    return RedirectToAction(nameof(Index));
                }

                await PopulateParentDepartments(department.ParentDepartmentId, id);
                return View(department);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading department data {DepartmentId}", id);
                TempData["Error"] = "An error occurred while loading department data";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Departments/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Department department)
        {
            if (id != department.DepartmentId)
            {
                TempData["Error"] = "Invalid data";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _departmentService.UpdateDepartmentAsync(department);
                    TempData["Success"] = "Department updated successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating department {DepartmentId}", id);
                    ModelState.AddModelError("", "An error occurred while updating the department");
                }
            }

            await PopulateParentDepartments(department.ParentDepartmentId, id);
            return View(department);
        }

        // GET: Departments/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var department = await _departmentService.GetDepartmentByIdAsync(id);
                if (department == null)
                {
                    TempData["Error"] = "Department not found";
                    return RedirectToAction(nameof(Index));
                }
                return View(department);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing department details {DepartmentId}", id);
                TempData["Error"] = "An error occurred while showing department details";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Departments/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _departmentService.DeleteDepartmentAsync(id);
                TempData["Success"] = "Department deleted successfully";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting department {DepartmentId}", id);
                TempData["Error"] = "An error occurred while deleting the department";
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateParentDepartments(int? selectedId = null, int? excludeId = null)
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();

            if (excludeId.HasValue)
                departments = departments.Where(d => d.DepartmentId != excludeId.Value).ToList();

            ViewBag.ParentDepartments = new SelectList(departments, "DepartmentId", "DepartmentName", selectedId);
        }
    }
}
