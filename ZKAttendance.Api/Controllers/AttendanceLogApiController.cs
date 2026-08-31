using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Dtos;
using ZKAttendance.Application.Services.Attendances;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Infrastructure.Services.Attendances;
using ZKAttendance.Infrastructure.Services.Common;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// The computed attendance log — punches grouped per employee-day with
    /// working hours and status. Ports Attendance/Index and Attendance/My.
    /// No business logic here: it calls the same query + calculation services
    /// the MVC screens used.
    /// </summary>
    [Route("api/Attendance")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Attendance")]
    [Authorize]
    public class AttendanceLogApiController : ControllerBase
    {
        private readonly AttendanceDbContext _context;
        private readonly AttendanceQueryService _query;
        private readonly AttendanceCalculationService _calc;
        private readonly LookupService _lookups;
        private readonly ILogger<AttendanceLogApiController> _logger;

        public AttendanceLogApiController(
            AttendanceDbContext context,
            AttendanceQueryService query,
            AttendanceCalculationService calc,
            LookupService lookups,
            ILogger<AttendanceLogApiController> logger)
        {
            _context = context;
            _query = query;
            _calc = calc;
            _lookups = lookups;
            _logger = logger;
        }

        /// <summary>
        /// Full attendance log, filtered and paged. Management only.
        /// </summary>
        /// <param name="search">Matches against the biometric id.</param>
        /// <param name="fromDate">Gregorian start date.</param>
        /// <param name="toDate">Gregorian end date.</param>
        /// <param name="branchId">Branch filter.</param>
        /// <param name="deviceId">Device filter.</param>
        /// <param name="status">"Full Day", "Check-in Only", …</param>
        /// <param name="minWorkHours">Minimum working hours.</param>
        /// <param name="maxWorkHours">Maximum working hours.</param>
        /// <param name="quickFilter">today | yesterday | thisweek | lastweek | thismonth | lastmonth | last7days | last30days</param>
        /// <param name="page">1-based page number. Page size is 50.</param>
        [HttpGet("log")]
        [Authorize(Roles = Roles.Management)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Log(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? branchId = null,
            [FromQuery] int? deviceId = null,
            [FromQuery] string? status = null,
            [FromQuery] int? minWorkHours = null,
            [FromQuery] int? maxWorkHours = null,
            [FromQuery] string? quickFilter = null,
            [FromQuery] int page = 1)
        {
            const int pageSize = 50;
            ApplyQuickFilter(quickFilter, ref fromDate, ref toDate);

            var logs = await _query.GetFilteredLogs(search, fromDate, toDate, branchId, deviceId);

            var employeeIds = logs.Where(l => l.EmployeeId.HasValue)
                .Select(l => l.EmployeeId!.Value).Distinct().ToList();
            var employees = await _query.GetEmployeesDictionary(employeeIds);
            var branches = await _query.GetBranchesDictionary(logs.Select(l => l.BranchId).Distinct().ToList());
            var devices = await _query.GetDevicesDictionary(logs.Select(l => l.DeviceId).Distinct().ToList());

            var rows = await _calc.BuildAttendanceViewModels(logs, employees, branches, devices);
            rows = ApplyExtraFilters(rows, status, minWorkHours, maxWorkHours);

            var ordered = rows
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.BiometricUserId)
                .ToList();

            var paged = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new
            {
                page,
                pageSize,
                totalRecords = ordered.Count,
                totalPages = (int)Math.Ceiling(ordered.Count / (double)pageSize),
                stats = new
                {
                    checkIns = ordered.Count(v => v.CheckInTime.HasValue),
                    checkOuts = ordered.Count(v => v.CheckOutTime.HasValue),
                    fullDay = ordered.Count(v => v.Status == "Full Day"),
                    checkInOnly = ordered.Count(v => v.Status == "Check-in Only"),
                    averageWorkHours = ordered.Where(v => v.WorkingHours > 0).Select(v => v.WorkingHours).DefaultIfEmpty(0).Average()
                },
                items = paged
            });
        }

        /// <summary>Filter option lists for the log screen.</summary>
        [HttpGet("log/filters")]
        [Authorize(Roles = Roles.Management)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Filters()
        {
            var branches = await _lookups.GetActiveBranchesAsync();
            var devices = await _lookups.GetActiveDevicesAsync();
            return Ok(new
            {
                branches = branches.Select(b => new { b.BranchId, b.BranchName }),
                devices = devices.Select(d => new { d.DeviceId, d.DeviceName })
            });
        }

        /// <summary>
        /// The signed-in employee's own attendance. Any authenticated account;
        /// returns an empty list with <c>linked = false</c> when the account is
        /// not tied to an employee record.
        /// </summary>
        /// <param name="fromDate">Gregorian start. Defaults to 30 days before <paramref name="toDate"/>.</param>
        /// <param name="toDate">Gregorian end. Defaults to today.</param>
        /// <param name="quickFilter">Same values as the log endpoint.</param>
        [HttpGet("my")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> My(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string? quickFilter = null)
        {
            ApplyQuickFilter(quickFilter, ref fromDate, ref toDate);
            var to = toDate ?? DateTime.Today;
            var from = fromDate ?? to.AddDays(-30);

            var employeeId = await CurrentEmployeeIdAsync();
            if (employeeId is null)
                return Ok(new { linked = false, fromAd = from.ToString("yyyy-MM-dd"), toAd = to.ToString("yyyy-MM-dd"), totalDays = 0, totalHours = 0.0, items = Array.Empty<AttendanceViewModel>() });

            var logs = await _context.AttendanceLogs.AsNoTracking()
                .Where(a => a.EmployeeId == employeeId.Value
                            && a.AttendanceTime.Date >= from.Date
                            && a.AttendanceTime.Date <= to.Date)
                .ToListAsync();

            var employees = await _query.GetEmployeesDictionary(new List<int> { employeeId.Value });
            var branches = await _query.GetBranchesDictionary(logs.Select(l => l.BranchId).Distinct().ToList());
            var devices = await _query.GetDevicesDictionary(logs.Select(l => l.DeviceId).Distinct().ToList());

            var rows = (await _calc.BuildAttendanceViewModels(logs, employees, branches, devices))
                .OrderByDescending(v => v.Date)
                .ToList();

            return Ok(new
            {
                linked = true,
                fromAd = from.ToString("yyyy-MM-dd"),
                toAd = to.ToString("yyyy-MM-dd"),
                totalDays = rows.Count,
                totalHours = rows.Sum(v => v.WorkingHours),
                items = rows
            });
        }

        private async Task<int?> CurrentEmployeeIdAsync()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var apiUserId))
                return null;

            return await _context.ApiUsers.AsNoTracking()
                .Where(u => u.ApiUserId == apiUserId)
                .Select(u => u.EmployeeId)
                .FirstOrDefaultAsync();
        }

        private static List<AttendanceViewModel> ApplyExtraFilters(
            List<AttendanceViewModel> list, string? status, int? min, int? max)
        {
            if (!string.IsNullOrEmpty(status))
                list = list.Where(v => v.Status == status).ToList();
            if (min.HasValue)
                list = list.Where(v => v.WorkingHours >= min.Value).ToList();
            if (max.HasValue)
                list = list.Where(v => v.WorkingHours <= max.Value).ToList();
            return list;
        }

        private static void ApplyQuickFilter(string? quickFilter, ref DateTime? fromDate, ref DateTime? toDate)
        {
            if (string.IsNullOrEmpty(quickFilter)) return;
            var today = DateTime.Today;

            switch (quickFilter.ToLowerInvariant())
            {
                case "today": fromDate = today; toDate = today; break;
                case "yesterday": fromDate = today.AddDays(-1); toDate = today.AddDays(-1); break;
                case "thisweek":
                    fromDate = today.AddDays(-(int)today.DayOfWeek); toDate = today; break;
                case "lastweek":
                    var lws = today.AddDays(-(int)today.DayOfWeek - 7);
                    fromDate = lws; toDate = lws.AddDays(6); break;
                case "thismonth":
                    fromDate = new DateTime(today.Year, today.Month, 1); toDate = today; break;
                case "lastmonth":
                    var lm = today.AddMonths(-1);
                    fromDate = new DateTime(lm.Year, lm.Month, 1);
                    toDate = new DateTime(lm.Year, lm.Month, DateTime.DaysInMonth(lm.Year, lm.Month)); break;
                case "last7days": fromDate = today.AddDays(-7); toDate = today; break;
                case "last30days": fromDate = today.AddDays(-30); toDate = today; break;
            }
        }
    }
}
