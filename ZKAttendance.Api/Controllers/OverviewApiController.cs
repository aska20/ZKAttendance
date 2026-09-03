using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// The attendance overview / pivot: employees down the side, days across the
    /// top, one status per cell. Absent = the day is a working day and the
    /// employee has no punch at all. First check-in and last check-out are the
    /// visible summary; every individual punch is still in the log and reachable
    /// through GET /api/Attendance/day.
    /// </summary>
    [Route("api/Overview")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Overview")]
    [Authorize(Roles = Roles.Management)]
    public class OverviewApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly INepaliCalendar _nepali;

        public OverviewApiController(AttendanceDbContext db, INepaliCalendar nepali)
        {
            _db = db;
            _nepali = nepali;
        }

        /// <param name="from">Gregorian start date. Defaults to 6 days ago.</param>
        /// <param name="to">Gregorian end date. Defaults to today.</param>
        /// <param name="departmentId">Only employees in this department.</param>
        /// <param name="search">Match employee name or biometric id (contains).</param>
        /// <param name="employeeId">A single employee.</param>
        /// <param name="includeInactive">Include deactivated employees.</param>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? departmentId = null,
            [FromQuery] string? search = null,
            [FromQuery] int? employeeId = null,
            [FromQuery] bool includeInactive = false)
        {
            var start = (from ?? DateTime.Today.AddDays(-6)).Date;
            var end = (to ?? DateTime.Today).Date;
            if (end < start) (start, end) = (end, start);
            if ((end - start).TotalDays > 92)
                return BadRequest(new { message = "Range too wide — 92 days maximum." });

            // ── days + holidays ────────────────────────────────────────
            var holidayRows = await _db.Holidays
                .Where(h => h.IsActive && h.HolidayDate >= start && h.HolidayDate <= end)
                .ToListAsync();
            var holidayByDate = holidayRows
                .GroupBy(h => h.HolidayDate.Date)
                .ToDictionary(g => g.Key, g => g.First());

            var days = new List<DayCell>();
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                var weeklyOff = _nepali.IsWeeklyOff(d);
                var named = holidayByDate.TryGetValue(d, out var h);
                days.Add(new DayCell
                {
                    Date = d,
                    DateIso = d.ToString("yyyy-MM-dd"),
                    DateBs = _nepali.ToBsString(d),
                    Weekday = d.DayOfWeek.ToString()[..3],
                    IsHoliday = weeklyOff || named,
                    HolidayName = named ? h!.HolidayName : (weeklyOff ? "Weekly off" : null),
                    HolidayType = named ? h!.HolidayType : (weeklyOff ? "Weekly off" : null)
                });
            }
            var workingDayCount = days.Count(x => !x.IsHoliday);

            // ── employees ──────────────────────────────────────────────
            var empQuery = _db.Employees
                .Include(e => e.Department)
                .Include(e => e.EmployeeBranches).ThenInclude(eb => eb.Branch)
                .AsQueryable();

            if (!includeInactive) empQuery = empQuery.Where(e => e.IsActive);
            if (employeeId.HasValue) empQuery = empQuery.Where(e => e.EmployeeId == employeeId.Value);
            if (departmentId.HasValue) empQuery = empQuery.Where(e => e.DepartmentId == departmentId.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                empQuery = empQuery.Where(e => e.EmployeeName.Contains(s) || e.BiometricUserId.Contains(s));
            }

            var employees = await empQuery
                .OrderBy(e => e.EmployeeName)
                .ToListAsync();

            var empIds = employees.Select(e => e.EmployeeId).ToList();

            // ── punches for the whole window, one query ─────────────────
            var logs = await _db.AttendanceLogs
                .Where(a => a.EmployeeId != null
                            && empIds.Contains(a.EmployeeId!.Value)
                            && a.AttendanceTime >= start
                            && a.AttendanceTime < end.AddDays(1))
                .Select(a => new { a.EmployeeId, a.AttendanceTime })
                .ToListAsync();

            var byEmpDay = logs
                .GroupBy(a => new { EmployeeId = a.EmployeeId!.Value, Day = a.AttendanceTime.Date })
                .ToDictionary(
                    g => (g.Key.EmployeeId, g.Key.Day),
                    g => g.Select(x => x.AttendanceTime).OrderBy(t => t).ToList());

            // ── build the grid ─────────────────────────────────────────
            var deptGroups = employees
                .GroupBy(e => new { Id = e.DepartmentId, Name = e.Department?.DepartmentName ?? "No department" })
                .OrderBy(g => g.Key.Name)
                .Select(g => new
                {
                    departmentId = g.Key.Id,
                    departmentName = g.Key.Name,
                    employees = g.Select(e =>
                    {
                        var cells = new Dictionary<string, object>();
                        int present = 0, absent = 0;

                        foreach (var day in days)
                        {
                            if (day.IsHoliday)
                            {
                                cells[day.DateIso] = new { status = "Holiday", firstIn = (DateTime?)null, lastOut = (DateTime?)null, hours = 0.0, punchCount = 0 };
                                continue;
                            }

                            if (byEmpDay.TryGetValue((e.EmployeeId, day.Date), out var times) && times.Count > 0)
                            {
                                var firstIn = times.First();
                                var lastOut = times.Count > 1 ? times.Last() : (DateTime?)null;
                                var hours = lastOut is { } lo ? Math.Round((lo - firstIn).TotalHours, 2) : 0.0;
                                cells[day.DateIso] = new { status = "Present", firstIn, lastOut, hours, punchCount = times.Count };
                                present++;
                            }
                            else
                            {
                                cells[day.DateIso] = new { status = "Absent", firstIn = (DateTime?)null, lastOut = (DateTime?)null, hours = 0.0, punchCount = 0 };
                                absent++;
                            }
                        }

                        var branch = e.EmployeeBranches?.FirstOrDefault(b => b.IsActive)?.Branch?.BranchName
                                     ?? e.EmployeeBranches?.FirstOrDefault()?.Branch?.BranchName;

                        return new
                        {
                            e.EmployeeId,
                            e.EmployeeName,
                            e.BiometricUserId,
                            departmentId = e.DepartmentId,
                            departmentName = e.Department?.DepartmentName,
                            branchName = branch,
                            e.Title,
                            totalDays = workingDayCount,
                            presentDays = present,
                            absentDays = absent,
                            cells
                        };
                    }).ToList()
                })
                .ToList();

            return Ok(new
            {
                fromAd = start.ToString("yyyy-MM-dd"),
                toAd = end.ToString("yyyy-MM-dd"),
                fromBs = _nepali.ToBsString(start),
                toBs = _nepali.ToBsString(end),
                workingDayCount,
                days,
                employeeCount = employees.Count,
                departments = deptGroups
            });
        }

        /// <summary>
        /// One row per employee for a whole period (a week, month or year):
        /// days present / absent, total hours, and the earliest check-in and
        /// latest check-out seen across the period. No per-day grid, so the
        /// range can be up to a year.
        /// </summary>
        /// <param name="from">Gregorian start. Defaults to the first of this month.</param>
        /// <param name="to">Gregorian end. Defaults to today.</param>
        /// <param name="departmentId">Only employees in this department.</param>
        [HttpGet("summary")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Summary(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? departmentId = null)
        {
            var today = DateTime.Today;
            var start = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
            var end = (to ?? today).Date;
            if (end < start) (start, end) = (end, start);
            if ((end - start).TotalDays > 400)
                return BadRequest(new { message = "Range too wide — one year maximum." });

            var holidayDates = new HashSet<DateTime>(
                (await _db.Holidays
                    .Where(h => h.IsActive && h.HolidayDate >= start && h.HolidayDate <= end)
                    .Select(h => h.HolidayDate)
                    .ToListAsync())
                .Select(d => d.Date));

            var workingDays = 0;
            for (var d = start; d <= end; d = d.AddDays(1))
                if (!_nepali.IsWeeklyOff(d) && !holidayDates.Contains(d)) workingDays++;

            var empQuery = _db.Employees.Include(e => e.Department).Where(e => e.IsActive);
            if (departmentId.HasValue) empQuery = empQuery.Where(e => e.DepartmentId == departmentId.Value);
            var employees = await empQuery.OrderBy(e => e.EmployeeName).ToListAsync();
            var empIds = employees.Select(e => e.EmployeeId).ToList();

            var logs = await _db.AttendanceLogs
                .Where(a => a.EmployeeId != null
                            && empIds.Contains(a.EmployeeId!.Value)
                            && a.AttendanceTime >= start
                            && a.AttendanceTime < end.AddDays(1))
                .Select(a => new { EmployeeId = a.EmployeeId!.Value, a.AttendanceTime })
                .ToListAsync();

            // per employee -> per day -> first/last
            var perEmpDay = logs
                .GroupBy(a => new { a.EmployeeId, Day = a.AttendanceTime.Date })
                .Select(g => new
                {
                    g.Key.EmployeeId,
                    g.Key.Day,
                    First = g.Min(x => x.AttendanceTime),
                    Last = g.Count() > 1 ? g.Max(x => x.AttendanceTime) : (DateTime?)null
                })
                .ToList();

            string? Hm(TimeSpan? t) => t is { } v ? $"{(int)v.TotalHours:D2}:{v.Minutes:D2}" : null;

            var deptGroups = employees
                .GroupBy(e => new { Id = e.DepartmentId, Name = e.Department?.DepartmentName ?? "No department" })
                .OrderBy(g => g.Key.Name)
                .Select(g => new
                {
                    departmentId = g.Key.Id,
                    departmentName = g.Key.Name,
                    employees = g.Select(e =>
                    {
                        var mine = perEmpDay.Where(x => x.EmployeeId == e.EmployeeId).ToList();
                        var present = mine.Count;
                        var totalHours = mine
                            .Where(x => x.Last is not null)
                            .Sum(x => Math.Round((x.Last!.Value - x.First).TotalHours, 2));

                        var ins = mine.Select(x => x.First.TimeOfDay).ToList();
                        var outs = mine.Where(x => x.Last is not null).Select(x => x.Last!.Value.TimeOfDay).ToList();

                        return new
                        {
                            e.EmployeeId,
                            e.EmployeeName,
                            e.BiometricUserId,
                            departmentId = e.DepartmentId,
                            departmentName = e.Department?.DepartmentName,
                            presentDays = present,
                            absentDays = Math.Max(0, workingDays - present),
                            totalHours = Math.Round(totalHours, 1),
                            earliestIn = Hm(ins.Count > 0 ? ins.Min() : null),
                            latestOut = Hm(outs.Count > 0 ? outs.Max() : null),
                            avgIn = Hm(ins.Count > 0 ? TimeSpan.FromTicks((long)ins.Average(t => t.Ticks)) : null),
                            avgOut = Hm(outs.Count > 0 ? TimeSpan.FromTicks((long)outs.Average(t => t.Ticks)) : null)
                        };
                    }).ToList()
                })
                .ToList();

            return Ok(new
            {
                fromAd = start.ToString("yyyy-MM-dd"),
                toAd = end.ToString("yyyy-MM-dd"),
                fromBs = _nepali.ToBsString(start),
                toBs = _nepali.ToBsString(end),
                workingDays,
                employeeCount = employees.Count,
                departments = deptGroups
            });
        }

        private sealed class DayCell
        {
            public DateTime Date { get; set; }
            public string DateIso { get; set; } = "";
            public string DateBs { get; set; } = "";
            public string Weekday { get; set; } = "";
            public bool IsHoliday { get; set; }
            public string? HolidayName { get; set; }
            public string? HolidayType { get; set; }
        }
    }
}
