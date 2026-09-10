using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Infrastructure.Services.Notifications;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Controllers
{
    public class AttendancePolicyRequest
    {
        /// <summary>"HH:mm", e.g. "10:00".</summary>
        public string OfficeStartTime { get; set; } = "10:00";
        public string OfficeEndTime { get; set; } = "18:00";
        public int GraceMinutes { get; set; } = 15;
        public int ApprovalRequiredAfterMinutes { get; set; } = 60;
        public bool RequireApprovalForLate { get; set; }
        public int CloseGraceMinutes { get; set; } = 30;
        public double HalfDayUnderHours { get; set; } = 4.0;
    }

    public class ApprovalDecisionRequest
    {
        public string? Note { get; set; }
    }

    public class BulkDecisionRequest
    {
        public List<int> ApprovalIds { get; set; } = new();
        public string? Note { get; set; }
    }

    /// <summary>Office hours, grace, and the late-arrival cut-off.</summary>
    [Route("api/Settings/attendance")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Settings")]
    [Authorize(Roles = Roles.Management)]
    public class AttendanceSettingsApiController : ControllerBase
    {
        private readonly IAttendancePolicyService _policy;
        private readonly ILogger<AttendanceSettingsApiController> _logger;

        public AttendanceSettingsApiController(
            IAttendancePolicyService policy,
            ILogger<AttendanceSettingsApiController> logger)
        {
            _policy = policy;
            _logger = logger;
        }

        /// <summary>Current rules, plus the resulting times worked out for display.</summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var p = await _policy.GetAsync(ct);
            return Ok(Shape(p));
        }

        /// <summary>Save the rules. Admin only.</summary>
        [HttpPut]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Update(
            [FromBody] AttendancePolicyRequest request, CancellationToken ct)
        {
            if (!TimeSpan.TryParse(request.OfficeStartTime, out var start))
                return BadRequest(ApiError.From("Office start time must look like 10:00."));
            if (!TimeSpan.TryParse(request.OfficeEndTime, out var end))
                return BadRequest(ApiError.From("Office end time must look like 18:00."));

            try
            {
                var saved = await _policy.SaveAsync(new AttendancePolicy
                {
                    OfficeStartTime = start,
                    OfficeEndTime = end,
                    GraceMinutes = request.GraceMinutes,
                    ApprovalRequiredAfterMinutes = request.ApprovalRequiredAfterMinutes,
                    RequireApprovalForLate = request.RequireApprovalForLate,
                    CloseGraceMinutes = request.CloseGraceMinutes,
                    HalfDayUnderHours = request.HalfDayUnderHours,
                }, User.Identity?.Name, ct);

                return Ok(Shape(saved));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        private static object Shape(AttendancePolicy p) => new
        {
            officeStartTime = p.OfficeStartTime.ToString(@"hh\:mm"),
            officeEndTime = p.OfficeEndTime.ToString(@"hh\:mm"),
            graceMinutes = p.GraceMinutes,
            approvalRequiredAfterMinutes = p.ApprovalRequiredAfterMinutes,
            requireApprovalForLate = p.RequireApprovalForLate,
            closeGraceMinutes = p.CloseGraceMinutes,
            halfDayUnderHours = p.HalfDayUnderHours,

            // Worked out here so the screen shows real clock times rather than
            // making the reader add minutes in their head.
            graceUntil = p.GraceUntil.ToString(@"hh\:mm"),
            approvalRequiredAfter = p.ApprovalRequiredAfter.ToString(@"hh\:mm"),
            dayClosesAt = p.DayClosesAt.ToString(@"hh\:mm"),
        };
    }

    /// <summary>The queue of late arrivals waiting on an admin decision.</summary>
    [Route("api/Attendance/approvals")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Attendance")]
    [Authorize(Roles = Roles.Management)]
    public class AttendanceApprovalsApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly IAttendancePolicyService _policy;
        private readonly INepaliCalendar _nepali;
        private readonly ILogger<AttendanceApprovalsApiController> _logger;

        public AttendanceApprovalsApiController(
            AttendanceDbContext db,
            IAttendancePolicyService policy,
            INepaliCalendar nepali,
            ILogger<AttendanceApprovalsApiController> logger)
        {
            _db = db;
            _policy = policy;
            _nepali = nepali;
            _logger = logger;
        }

        /// <param name="status">Pending, Approved, Rejected, or All. Defaults to Pending.</param>
        /// <param name="from">Gregorian start. Defaults to 30 days ago.</param>
        /// <param name="to">Gregorian end. Defaults to today.</param>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> List(
            [FromQuery] string status = "Pending",
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? departmentId = null,
            CancellationToken ct = default)
        {
            var start = (from ?? DateTime.Today.AddDays(-30)).Date;
            var end = (to ?? DateTime.Today).Date;
            if (end < start) (start, end) = (end, start);

            try
            {

            // Re-check the last few days so the queue reflects what the
            // devices have reported, without needing a separate job.
            for (var d = DateTime.Today; d >= DateTime.Today.AddDays(-2) && d >= start; d = d.AddDays(-1))
            {
                try { await _policy.EvaluateDayAsync(d, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not evaluate {Date}", d); }
            }

            var q = _db.AttendanceApprovals
                .Include(a => a.Employee).ThenInclude(e => e!.Department)
                .Where(a => a.AttendanceDate >= start && a.AttendanceDate <= end);

            if (!status.Equals("All", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<AttendanceApprovalStatus>(status, true, out var parsed))
                q = q.Where(a => a.Status == parsed);

            if (departmentId.HasValue)
                q = q.Where(a => a.Employee!.DepartmentId == departmentId.Value);

            var rows = await q
                .OrderByDescending(a => a.AttendanceDate)
                .ThenBy(a => a.Employee!.EmployeeName)
                .Take(500)
                .ToListAsync(ct);

            var policy = await _policy.GetAsync(ct);

            return Ok(new
            {
                pendingCount = await _db.AttendanceApprovals
                    .CountAsync(a => a.Status == AttendanceApprovalStatus.Pending, ct),
                officeStartTime = policy.OfficeStartTime.ToString(@"hh\:mm"),
                approvalRequiredAfter = policy.ApprovalRequiredAfter.ToString(@"hh\:mm"),
                items = rows.Select(a => new
                {
                    a.ApprovalId,
                    a.EmployeeId,
                    employeeName = a.Employee?.EmployeeName,
                    departmentName = a.Employee?.Department?.DepartmentName,
                    photoUrl = a.Employee?.PhotoUrl,
                    date = a.AttendanceDate.ToString("yyyy-MM-dd"),
                    dateBs = _nepali.ToBsString(a.AttendanceDate),
                    a.FirstCheckIn,
                    a.MinutesLate,
                    status = a.Status.ToString(),
                    a.DecidedBy,
                    a.DecidedDate,
                    a.Note
                })
            });
            }
            catch (Exception ex) when (IsMissingTable(ex))
            {
                return StatusCode(503, ApiError.From(MissingTableMessage));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading the approval queue failed");
                return StatusCode(500, ApiError.From($"Could not load approvals: {ex.Message}"));
            }
        }

        /// <summary>
        /// The AttendanceApprovals table is created by a migration. Until it is
        /// applied, every query here fails, and "Invalid object name" is not a
        /// useful thing to show an operator.
        /// </summary>
        private static bool IsMissingTable(Exception ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
                if (e.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                    && e.Message.Contains("AttendanceApproval", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private const string MissingTableMessage =
            "The AttendanceApprovals table does not exist yet. Run 'Add-Migration AddAttendanceApprovals' " +
            "then 'Update-Database', or execute Migrations/AddAttendanceApprovals.sql.";

        /// <summary>Count only. Used for the badge in the sidebar.</summary>
        [HttpGet("count")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Count(CancellationToken ct)
        {
            try
            {
                return Ok(new
                {
                    pending = await _db.AttendanceApprovals
                        .CountAsync(a => a.Status == AttendanceApprovalStatus.Pending, ct)
                });
            }
            catch (Exception ex)
            {
                // A badge is not worth breaking a page over.
                _logger.LogDebug(ex, "Approval count unavailable");
                return Ok(new { pending = 0 });
            }
        }

        /// <summary>Re-check a day against the current rules and refresh the queue.</summary>
        [HttpPost("evaluate")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Evaluate([FromQuery] DateTime? date, CancellationToken ct)
        {
            var day = (date ?? DateTime.Today).Date;
            var created = await _policy.EvaluateDayAsync(day, ct);
            return Ok(new { date = day.ToString("yyyy-MM-dd"), needingApproval = created });
        }

        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public Task<IActionResult> Approve(int id, [FromBody] ApprovalDecisionRequest? body, CancellationToken ct)
            => Decide(id, AttendanceApprovalStatus.Approved, body?.Note, ct);

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public Task<IActionResult> Reject(int id, [FromBody] ApprovalDecisionRequest? body, CancellationToken ct)
            => Decide(id, AttendanceApprovalStatus.Rejected, body?.Note, ct);

        /// <summary>Approve several at once. Nothing else in the queue is touched.</summary>
        [HttpPost("approve-many")]
        [Authorize(Roles = Roles.Admin)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> ApproveMany([FromBody] BulkDecisionRequest body, CancellationToken ct)
        {
            if (body.ApprovalIds.Count == 0)
                return BadRequest(ApiError.From("Select at least one row."));

            var rows = await _db.AttendanceApprovals
                .Where(a => body.ApprovalIds.Contains(a.ApprovalId)
                            && a.Status == AttendanceApprovalStatus.Pending)
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                row.Status = AttendanceApprovalStatus.Approved;
                row.DecidedBy = User.Identity?.Name;
                row.DecidedDate = DateTime.Now;
                row.Note = body.Note;
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("{Count} attendance approval(s) approved by {User}", rows.Count, User.Identity?.Name);

            return Ok(new { approved = rows.Count });
        }

        private async Task<IActionResult> Decide(
            int id, AttendanceApprovalStatus status, string? note, CancellationToken ct)
        {
            var row = await _db.AttendanceApprovals
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(a => a.ApprovalId == id, ct);

            if (row is null) return NotFound(ApiError.From("That approval no longer exists."));

            row.Status = status;
            row.DecidedBy = User.Identity?.Name;
            row.DecidedDate = DateTime.Now;
            row.Note = note;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "{Status} attendance for {Employee} on {Date} by {User}",
                status, row.Employee?.EmployeeName, row.AttendanceDate.ToString("dd/MM/yyyy"), User.Identity?.Name);

            return Ok(new
            {
                row.ApprovalId,
                status = row.Status.ToString(),
                row.DecidedBy,
                row.DecidedDate
            });
        }
    }

    public class EmailSettingsRequest
    {
        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string? Username { get; set; }
        /// <summary>Leave null or blank to keep the stored password unchanged.</summary>
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string? FromName { get; set; }
    }

    /// <summary>
    /// SMTP settings, kept in the database rather than appsettings.json so the
    /// mail server can be changed without a redeploy.
    /// </summary>
    [Route("api/Settings/email")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Settings")]
    [Authorize(Roles = Roles.Admin)]
    public class EmailSettingsApiController : ControllerBase
    {
        private readonly AttendanceDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IEmailSender _sender;
        private readonly ILogger<EmailSettingsApiController> _logger;

        public EmailSettingsApiController(
            AttendanceDbContext db,
            IMemoryCache cache,
            IEmailSender sender,
            ILogger<EmailSettingsApiController> logger)
        {
            _db = db;
            _cache = cache;
            _sender = sender;
            _logger = logger;
        }

        /// <summary>
        /// Current settings. The password is NEVER returned, only whether one
        /// is stored, so opening the page cannot leak it to the browser.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var rows = await _db.SystemSettings
                .Where(s => s.Category == SmtpEmailSender.Category && s.IsActive)
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct);

            string? V(string k) => rows.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

            return Ok(new
            {
                smtpHost = V("SmtpHost"),
                smtpPort = int.TryParse(V("SmtpPort"), out var p) ? p : 587,
                useSsl = !string.Equals(V("UseSsl"), "false", StringComparison.OrdinalIgnoreCase),
                username = V("Username"),
                fromAddress = V("FromAddress"),
                fromName = V("FromName") ?? "Attendance System",
                passwordIsSet = !string.IsNullOrWhiteSpace(V("Password")),
                configured = await _sender.IsConfiguredAsync(ct)
            });
        }

        [HttpPut]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Update(
            [FromBody] EmailSettingsRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.SmtpHost))
                return BadRequest(ApiError.From("An SMTP host is required, for example smtp.gmail.com."));

            var from = request.FromAddress?.Trim();
            if (string.IsNullOrWhiteSpace(from)) from = request.Username?.Trim();
            if (string.IsNullOrWhiteSpace(from))
                return BadRequest(ApiError.From("A from-address is required. Mail servers reject messages without one."));

            if (request.SmtpPort is < 1 or > 65535)
                return BadRequest(ApiError.From("The port must be between 1 and 65535. 587 is usual."));

            var pairs = new Dictionary<string, string?>
            {
                ["SmtpHost"] = request.SmtpHost.Trim(),
                ["SmtpPort"] = request.SmtpPort.ToString(),
                ["UseSsl"] = request.UseSsl ? "true" : "false",
                ["Username"] = request.Username?.Trim(),
                ["FromAddress"] = from,
                ["FromName"] = string.IsNullOrWhiteSpace(request.FromName) ? "Attendance System" : request.FromName.Trim()
            };

            // A blank password means "leave it alone", so re-saving the form
            // after loading it does not wipe the stored one.
            if (!string.IsNullOrWhiteSpace(request.Password))
                pairs["Password"] = request.Password;

            var existing = await _db.SystemSettings
                .Where(s => s.Category == SmtpEmailSender.Category)
                .ToListAsync(ct);

            foreach (var (key, value) in pairs)
            {
                var row = existing.FirstOrDefault(s => s.SettingKey == key);
                if (row is null)
                {
                    _db.SystemSettings.Add(new SystemSetting
                    {
                        SettingKey = key,
                        SettingValue = value ?? string.Empty,
                        Category = SmtpEmailSender.Category,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }
                else
                {
                    row.SettingValue = value ?? string.Empty;
                    row.IsActive = true;
                    row.ModifiedDate = DateTime.Now;
                }
            }

            await _db.SaveChangesAsync(ct);
            _cache.Remove(SmtpEmailSender.CacheKey);

            _logger.LogInformation("Email settings updated by {User}", User.Identity?.Name);

            return Ok(new { configured = await _sender.IsConfiguredAsync(ct), message = "Email settings saved." });
        }

        /// <summary>
        /// Send one message to prove the settings work, before the end-of-day
        /// job tries it on two hundred people.
        /// </summary>
        [HttpPost("test")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Test([FromQuery] string? to, CancellationToken ct)
        {
            var recipient = to?.Trim();
            if (string.IsNullOrWhiteSpace(recipient))
                return BadRequest(ApiError.From("Enter an address to send the test to."));

            try
            {
                await _sender.SendAsync(
                    recipient,
                    "Attendance system test email",
                    "<p>This is a test from the attendance system.</p>" +
                    "<p>If you can read this, SMTP is working and the end-of-day emails will send.</p>",
                    ct);

                return Ok(new { sent = true, message = $"Test email sent to {recipient}. Check the inbox and the spam folder." });
            }
            catch (Exception ex)
            {
                // The SMTP error is the single most useful thing here, so pass
                // it through rather than replacing it with something generic.
                _logger.LogWarning(ex, "Test email to {To} failed", recipient);
                return BadRequest(ApiError.From($"Sending failed: {ex.Message}"));
            }
        }
    }
}
