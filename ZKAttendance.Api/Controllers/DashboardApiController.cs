using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>Landing-page counters and recent activity for the management dashboard.</summary>
    [Route("api/Dashboard")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Dashboard")]
    [Authorize(Roles = Roles.Management)]
    public class DashboardApiController : ControllerBase
    {
        private readonly AttendanceDbContext _context;
        private readonly INepaliCalendar _nepali;
        private readonly ILogger<DashboardApiController> _logger;

        public DashboardApiController(
            AttendanceDbContext context,
            INepaliCalendar nepali,
            ILogger<DashboardApiController> logger)
        {
            _context = context;
            _nepali = nepali;
            _logger = logger;
        }

        /// <summary>Everything the dashboard screen needs in one call.</summary>
        /// <remarks>
        /// Counters, today's present/absent split, the last sync time and the
        /// ten most recent punches. Late and early-leave counts are placeholders
        /// until shift rules are applied to the calculation.
        /// </remarks>
        [HttpGet("summary")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Summary()
        {
            var today = DateTime.Today;

            // Active only. A deactivated ("deleted") employee still has payroll
            // history but must not count towards headcount or today's absentees.
            var totalEmployees = await _context.Employees.CountAsync(e => e.IsActive);
            var activeEmployeeIds = await _context.Employees
                .Where(e => e.IsActive)
                .Select(e => e.EmployeeId)
                .ToListAsync();

            var todayLogs = await _context.AttendanceLogs
                .Where(a => a.AttendanceTime.Date == today)
                .ToListAsync();

            // Count people, not raw punch ids. A punch whose biometric id is not
            // mapped to an employee yet (EmployeeId == null) is real data but it
            // is not one of "our" employees, so it must not inflate the present
            // count or push the absent count negative.
            var presentToday = todayLogs
                .Where(l => l.EmployeeId.HasValue && activeEmployeeIds.Contains(l.EmployeeId.Value))
                .Select(l => l.EmployeeId!.Value)
                .Distinct()
                .Count();

            var unattributedToday = todayLogs
                .Where(l => l.EmployeeId is null)
                .Select(l => l.BiometricUserId)
                .Distinct()
                .Count();

            // People enrolled on a device who were never added to the system —
            // a standing reminder until an admin/HR completes them.
            var knownIds = new HashSet<string>(await _context.Employees.Select(e => e.BiometricUserId).ToListAsync());
            foreach (var id in await _context.EmployeeDevices.Select(ed => ed.BiometricUserId).ToListAsync())
                knownIds.Add(id);
            var unregisteredCount = (await _context.AttendanceLogs
                    .Where(a => a.EmployeeId == null)
                    .Select(a => a.BiometricUserId)
                    .Distinct()
                    .ToListAsync())
                .Count(id => !knownIds.Contains(id));

            var lastSyncTime = await _context.AttendanceLogs
                .Where(a => a.IsSynced)
                .OrderByDescending(a => a.SyncedDate)
                .Select(a => a.SyncedDate)
                .FirstOrDefaultAsync();

            var recentLogs = await _context.AttendanceLogs
                .Include(a => a.Branch)
                .Include(a => a.Device)
                .Where(a => a.AttendanceTime.Date == today)
                .OrderByDescending(a => a.AttendanceTime)
                .Take(10)
                .ToListAsync();

            var deviceErrors = await _context.DeviceErrors
                .Include(e => e.Device)
                .Where(e => !e.IsResolved)
                .OrderByDescending(e => e.ErrorDateTime)
                .Take(5)
                .ToListAsync();

            return Ok(new
            {
                dateAd = today.ToString("yyyy-MM-dd"),
                dateBs = _nepali.ToBsString(today),
                counters = new
                {
                    totalEmployees,
                    totalBranches = await _context.Branches.CountAsync(),
                    totalDevices = await _context.Devices.CountAsync(),
                    activeDevices = await _context.Devices.CountAsync(d => d.IsActive),
                    inactiveDevices = await _context.Devices.CountAsync(d => !d.IsActive)
                },
                unregisteredCount,
                today = new
                {
                    present = presentToday,
                    absent = Math.Max(0, totalEmployees - presentToday),
                    unattributed = unattributedToday, // punched but not linked to an employee
                    late = 0,        // TODO: apply late-arrival shift rules
                    earlyLeave = 0   // TODO: apply early-departure shift rules
                },
                lastSyncTime,
                recentLogs = recentLogs.Select(l => new
                {
                    l.LogId,
                    l.EmployeeId,
                    l.BiometricUserId,
                    branch = l.Branch?.BranchName,
                    device = l.Device?.DeviceName,
                    punchTimeAd = l.AttendanceTime,
                    punchTimeBs = _nepali.ToBsString(l.AttendanceTime),
                    l.AttendanceType
                }),
                deviceErrors = deviceErrors.Select(e => new
                {
                    e.ErrorId,
                    device = e.Device?.DeviceName,
                    e.ErrorMessage,
                    e.Severity,
                    e.ErrorDateTime
                })
            });
        }
    }
}
