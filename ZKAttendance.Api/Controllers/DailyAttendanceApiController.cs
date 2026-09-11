using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Daily and monthly attendance reporting, plus the manual equivalents of
    /// the scheduled end-of-day job.
    ///
    /// Every endpoint here calls IDailyAttendanceService, which is the same
    /// service the background job calls. The business logic exists once.
    /// </summary>
    [Route("api/Attendance/daily")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Attendance")]
    [Authorize(Roles = Roles.Management)]
    public class DailyAttendanceApiController : ControllerBase
    {
        private readonly IDailyAttendanceService _service;
        private readonly IEmailSender _email;
        private readonly ILogger<DailyAttendanceApiController> _logger;

        public DailyAttendanceApiController(
            IDailyAttendanceService service,
            IEmailSender email,
            ILogger<DailyAttendanceApiController> logger)
        {
            _service = service;
            _email = email;
            _logger = logger;
        }

        /// <summary>
        /// Every active employee's day, including those who never scanned.
        /// </summary>
        /// <param name="date">Gregorian. Defaults to today.</param>
        [HttpGet("report")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> DailyReport(
            [FromQuery] DateTime? date,
            [FromQuery] int? departmentId,
            CancellationToken ct)
        {
            try
            {
                var report = await _service.BuildDailyReportAsync(date ?? DateTime.Today, departmentId, ct);
                return Ok(Shape(report));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily report failed");
                return StatusCode(500, ApiError.From($"Could not build the report: {ex.Message}"));
            }
        }

        /// <summary>Cross-tab: employees down, dates across, one mark per cell.</summary>
        [HttpGet("monthly")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> MonthlyReport(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? departmentId,
            CancellationToken ct)
        {
            var start = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = to ?? DateTime.Today;

            try
            {
                var report = await _service.BuildMonthlyReportAsync(start, end, departmentId, ct);
                return Ok(new
                {
                    fromAd = report.From.ToString("yyyy-MM-dd"),
                    toAd = report.To.ToString("yyyy-MM-dd"),
                    report.FromBs,
                    report.ToBs,
                    report.WorkingDays,
                    dates = report.Dates,
                    holidayDates = report.HolidayDates,
                    rows = report.Rows.Select(r => new
                    {
                        r.EmployeeId,
                        r.EmployeeName,
                        r.DepartmentName,
                        r.DeviceUserId,
                        r.Marks,
                        r.TotalPresent,
                        r.TotalLate,
                        r.TotalPartial,
                        r.TotalAbsent,
                        r.TotalHours
                    })
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Monthly report failed");
                return StatusCode(500, ApiError.From($"Could not build the report: {ex.Message}"));
            }
        }

        /// <summary>
        /// The manual equivalent of the scheduled job. Same service, same
        /// result, so a manual run and an automatic run cannot disagree.
        /// </summary>
        /// <param name="sendEmails">False to recalculate without emailing anyone.</param>
        [HttpPost("process")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Process(
            [FromQuery] DateTime? date,
            [FromQuery] bool sendEmails = false,
            CancellationToken ct = default)
        {
            var day = (date ?? DateTime.Today).Date;

            try
            {
                var report = await _service.RunEndOfDayAsync(day, sendEmails, ct);
                _logger.LogInformation(
                    "Manual attendance run for {Date} by {User}", day.ToString("dd/MM/yyyy"), User.Identity?.Name);

                return Ok(new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    report.DateBs,
                    report.TotalEmployees,
                    report.PresentCount,
                    report.LateCount,
                    report.PartialCount,
                    report.AbsentCount,
                    report.UnmappedDeviceIds,
                    emailsSent = sendEmails,
                    message = sendEmails
                        ? "Attendance processed and emails sent."
                        : "Attendance processed. No emails were sent."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual attendance run failed for {Date}", day);
                return StatusCode(500, ApiError.From($"The run failed: {ex.Message}"));
            }
        }

        /// <summary>Send each employee their own attendance for the day.</summary>
        [HttpPost("send-emails")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> SendEmails([FromQuery] DateTime? date, CancellationToken ct)
        {
            var day = (date ?? DateTime.Today).Date;

            try
            {
                var staff = await _service.SendEmployeeEmailsAsync(day, ct);
                var admin = await _service.SendAdminSummaryAsync(day, ct);

                return Ok(new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    staff.Sent,
                    staff.Skipped,
                    staff.Failed,
                    adminSummarySent = admin.Sent,
                    errors = staff.Errors.Concat(admin.Errors).Take(20),
                    message = staff.Sent == 0 && staff.Failed == 0
                        ? "Nothing was sent. Check that employees have email addresses and that SMTP is configured."
                        : $"{staff.Sent} email(s) sent, {staff.Failed} failed."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email run failed for {Date}", day);
                return StatusCode(500, ApiError.From($"Sending failed: {ex.Message}"));
            }
        }

        /// <summary>Whether email is set up, so the UI can disable the button rather than fail.</summary>
        [HttpGet("email-status")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> EmailStatus(CancellationToken ct)
        {
            var configured = await _email.IsConfiguredAsync(ct);
            return Ok(new
            {
                configured,
                message = configured
                    ? "Email is configured."
                    : "Set the SMTP host and from-address in Settings to enable emails."
            });
        }

        private static object Shape(DailyAttendanceReport r) => new
        {
            date = r.Date.ToString("yyyy-MM-dd"),
            r.DateBs,
            r.IsHoliday,
            r.HolidayName,
            r.TotalEmployees,
            r.PresentCount,
            r.LateCount,
            r.PartialCount,
            r.AbsentCount,
            r.UnmappedDeviceIds,
            rows = r.Rows.Select(x => new
            {
                x.EmployeeId,
                x.EmployeeName,
                x.DeviceUserId,
                x.DepartmentName,
                x.Email,
                x.CheckIn,
                x.CheckOut,
                punchCount = x.Punches.Count,
                status = x.StatusText,
                mark = x.Mark,
                x.MinutesLate,
                x.WorkedHours,
                x.DeviceName,
                x.NeedsApproval,
                x.ApprovalStatus
            })
        };
    }
}
