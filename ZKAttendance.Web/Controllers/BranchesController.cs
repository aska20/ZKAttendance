using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Web.Controllers
{
    [Authorize]
    public class BranchesController : Controller
    {
        private readonly IBrancheService _branchService;
        private readonly AttendanceDbContext _context;
        private readonly ILogger<BranchesController> _logger;

        public BranchesController(
            IBrancheService branchService,
            AttendanceDbContext context,
            ILogger<BranchesController> logger)
        {
            _branchService = branchService;
            _context = context;
            _logger = logger;
        }

        // GET: Branches
        public async Task<IActionResult> Index()
        {
            try
            {
                var branches = await _context.Branches
                    .Include(b => b.Devices.Where(d => d.IsActive))
                    .Include(b => b.EmployeeBranches.Where(eb => eb.IsActive))
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.City)
                    .ThenBy(b => b.BranchName)
                    .AsNoTracking()
                    .ToListAsync();

                return View(branches);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing branches");
                TempData["Error"] = "An error occurred while loading the branch list";
                return View(new List<Branch>());
            }
        }

        // GET: Branches/Create
        public IActionResult Create()
        {
            var branch = new Branch
            {
                IsActive = true
            };
            return View(branch);
        }

        // POST: Branches/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Branch branch)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _branchService.CreateBranchAsync(branch);
                    TempData["Success"] = "Branch created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("BranchCode", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating branch");
                    ModelState.AddModelError("", "An error occurred while creating the branch");
                }
            }
            return View(branch);
        }

        // GET: Branches/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var branch = await _branchService.GetBranchByIdAsync(id);
                if (branch == null)
                {
                    TempData["Error"] = "Branch not found";
                    return RedirectToAction(nameof(Index));
                }
                return View(branch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading branch data {BranchId}", id);
                TempData["Error"] = "An error occurred while loading branch data";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Branches/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Branch branch)
        {
            if (id != branch.BranchId)
            {
                TempData["Error"] = "Invalid data";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _branchService.UpdateBranchAsync(branch);
                    TempData["Success"] = "Branch updated successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("BranchCode", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating branch {BranchId}", id);
                    ModelState.AddModelError("", "An error occurred while updating the branch");
                }
            }
            return View(branch);
        }

        // GET: Branches/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var branch = await _context.Branches
                    .Include(b => b.Devices.Where(d => d.IsActive))
                    .Include(b => b.EmployeeBranches.Where(eb => eb.IsActive))
                        .ThenInclude(eb => eb.Employee)
                            .ThenInclude(e => e.Department)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BranchId == id);

                if (branch == null)
                {
                    TempData["Error"] = "Branch not found";
                    return RedirectToAction(nameof(Index));
                }

                return View(branch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing branch details {BranchId}", id);
                TempData["Error"] = "An error occurred while showing branch details";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Branches/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _branchService.DeleteBranchAsync(id);
                TempData["Success"] = "Branch deleted successfully";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting branch {BranchId}", id);
                TempData["Error"] = "An error occurred while deleting the branch";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
