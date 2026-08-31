using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>Attendance reports. Thin wrapper over IReportService / IAttendanceService.</summary>
    [Route("api/Reports")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Reports")]
    [Authorize(Roles = Roles.Management)]
    public class ReportsApiController : ControllerBase
    {
        private readonly IReportService _reports;
        private readonly IAttendanceService _attendance;
        private readonly INepaliCalendar _nepali;

        public ReportsApiController(
            IReportService reports,
            IAttendanceService attendance,
            INepaliCalendar nepali)
        {
            _reports = reports;
            _attendance = attendance;
            _nepali = nepali;
        }

        /// <summary>Daily attendance report for one day (present + absent rows).</summary>
        /// <param name="date">Gregorian date. Defaults to today.</param>
        /// <param name="branchId">Optional branch filter.</param>
        /// <param name="department">Optional department name filter.</param>
        [HttpGet("daily")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Daily(
            [FromQuery] DateTime? date = null,
            [FromQuery] int? branchId = null,
            [FromQuery] string? department = null)
        {
            var day = date ?? DateTime.Today;
            var report = await _reports.GetDailyAttendanceReportAsync(day, branchId, department);
            return Ok(WithBs(report, day));
        }

        /// <summary>Attendance report across a date range.</summary>
        /// <param name="from">Gregorian start. Defaults to 7 days ago.</param>
        /// <param name="to">Gregorian end. Defaults to today.</param>
        /// <param name="branchId">Optional branch filter.</param>
        /// <param name="deviceId">Optional device filter.</param>
        /// <param name="employeeId">Optional single-employee filter.</param>
        [HttpGet("range")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Range(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? branchId = null,
            [FromQuery] int? deviceId = null,
            [FromQuery] int? employeeId = null)
        {
            var start = from ?? DateTime.Today.AddDays(-7);
            var end = to ?? DateTime.Today;

            var report = await _reports.GetDailyAttendanceRangeReportAsync(
                start, end, branchId, deviceId, employeeId);

            return Ok(new
            {
                fromAd = start.ToString("yyyy-MM-dd"),
                toAd = end.ToString("yyyy-MM-dd"),
                fromBs = _nepali.ToBsString(start),
                toBs = _nepali.ToBsString(end),
                report
            });
        }

        /// <summary>Present / absent / late counts plus the present and absent lists for one day.</summary>
        [HttpGet("daily-summary")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> DailySummary(
            [FromQuery] DateTime? date = null,
            [FromQuery] int? branchId = null,
            [FromQuery] int? departmentId = null)
        {
            var day = date ?? DateTime.Today;
            var summary = await _attendance.GetDailyAttendanceReportSummaryAsync(day, branchId, departmentId);

            return Ok(new
            {
                dateAd = day.ToString("yyyy-MM-dd"),
                dateBs = _nepali.ToBsString(day),
                isWeeklyOff = _nepali.IsWeeklyOff(day),
                summary
            });
        }

        private object WithBs(Application.Dtos.Reports.DailyAttendanceReportDto report, DateTime day) => new
        {
            dateAd = day.ToString("yyyy-MM-dd"),
            dateBs = _nepali.ToBsString(day),
            report.TotalEmployees,
            report.PresentCount,
            report.AbsentCount,
            report.AttendanceRate,
            report.Items
        };
    }
}
