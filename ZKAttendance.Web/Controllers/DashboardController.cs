using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Web.Controllers
{
    [Authorize(Roles = "Admin,HR")]
    public class DashboardController : Controller
    {
        private readonly AttendanceDbContext _context;

        public DashboardController(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var today = DateTime.Today;

                // General statistics
                ViewBag.TotalEmployees = await _context.Employees.CountAsync();
                ViewBag.TotalBranches = await _context.Branches.CountAsync();
                ViewBag.TotalDevices = await _context.Devices.CountAsync();
                ViewBag.ActiveDevices = await _context.Devices.CountAsync(d => d.IsActive);
                ViewBag.InactiveDevices = await _context.Devices.CountAsync(d => !d.IsActive);

                // Today's attendance statistics
                var todayLogs = await _context.AttendanceLogs
                    .Where(a => a.AttendanceTime.Date == today)
                    .ToListAsync();

                var todayEmployeeIds = todayLogs
                    .Select(l => l.BiometricUserId)
                    .Distinct()
                    .Count();

                ViewBag.TodayAttendance = todayEmployeeIds;
                ViewBag.TodayAbsent = ViewBag.TotalEmployees - todayEmployeeIds;

                // Late and early-departure counts (should use shift rules)
                ViewBag.TodayLate = 0; // TODO: apply late-arrival rules
                ViewBag.TodayEarlyLeave = 0; // TODO: apply early-departure rules

                // Last sync time
                ViewBag.LastSyncTime = await _context.AttendanceLogs
                    .Where(a => a.IsSynced)
                    .OrderByDescending(a => a.SyncedDate)
                    .Select(a => a.SyncedDate)
                    .FirstOrDefaultAsync();

                // Most recent records
                ViewBag.RecentLogs = await _context.AttendanceLogs
                    .Include(a => a.Branch)
                    .Include(a => a.Device)
                    .Where(a => a.AttendanceTime.Date == today)
                    .OrderByDescending(a => a.AttendanceTime)
                    .Take(10)
                    .ToListAsync();

                // Device errors (uses ErrorDateTime, not ErrorTime)
                ViewBag.DeviceErrors = await _context.DeviceErrors
                    .Include(e => e.Device)
                    .Where(e => !e.IsResolved)
                    .OrderByDescending(e => e.ErrorDateTime)
                    .Take(5)
                    .ToListAsync();

                return View();
            }
            catch (Exception ex)
            {
                // No logger used here
                // A logger can be added later
                Console.WriteLine($"Error loading the dashboard: {ex.Message}");
                return View();
            }
        }
    }
}
