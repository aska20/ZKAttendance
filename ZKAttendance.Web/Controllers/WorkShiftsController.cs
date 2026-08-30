using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Web.Controllers
{
    [Authorize]
    public class WorkShiftsController : Controller
    {
        private readonly IWorkShiftService _shiftService;
        private readonly ILogger<WorkShiftsController> _logger;

        public WorkShiftsController(
            IWorkShiftService shiftService,
            ILogger<WorkShiftsController> logger)
        {
            _shiftService = shiftService;
            _logger = logger;
        }

        // GET: WorkShifts
        public async Task<IActionResult> Index()
        {
            try
            {
                var shifts = await _shiftService.GetAllShiftsAsync();
                return View(shifts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing shifts");
                TempData["Error"] = "An error occurred while loading the shift list";
                return View(new List<WorkShift>());
            }
        }

        // GET: WorkShifts/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: WorkShifts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WorkShift shift)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _shiftService.CreateShiftAsync(shift);
                    TempData["Success"] = "Shift created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating shift");
                    ModelState.AddModelError("", "An error occurred while creating the shift");
                }
            }
            return View(shift);
        }

        // GET: WorkShifts/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var shift = await _shiftService.GetShiftByIdAsync(id);
                if (shift == null)
                {
                    TempData["Error"] = "Shift not found";
                    return RedirectToAction(nameof(Index));
                }
                return View(shift);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading shift data {ShiftId}", id);
                TempData["Error"] = "An error occurred while loading shift data";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: WorkShifts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, WorkShift shift)
        {
            if (id != shift.ShiftId)
            {
                TempData["Error"] = "Invalid data";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _shiftService.UpdateShiftAsync(shift);
                    TempData["Success"] = "Shift updated successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating shift {ShiftId}", id);
                    ModelState.AddModelError("", "An error occurred while updating the shift");
                }
            }
            return View(shift);
        }

        // GET: WorkShifts/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var shift = await _shiftService.GetShiftByIdAsync(id);
                if (shift == null)
                {
                    TempData["Error"] = "Shift not found";
                    return RedirectToAction(nameof(Index));
                }
                return View(shift);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing shift details {ShiftId}", id);
                TempData["Error"] = "An error occurred while showing shift details";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: WorkShifts/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _shiftService.DeleteShiftAsync(id);
                TempData["Success"] = "Shift deleted successfully";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting shift {ShiftId}", id);
                TempData["Error"] = "An error occurred while deleting the shift";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
