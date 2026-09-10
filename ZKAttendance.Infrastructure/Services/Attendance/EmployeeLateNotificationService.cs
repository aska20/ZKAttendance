using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Infrastructure.Services.Attendance
{
    public class EmployeeLateNotificationService : IEmployeeLateNotificationService
    {
        private readonly AttendanceDbContext _db;
        private readonly IAttendancePolicyService _policyService;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<EmployeeLateNotificationService> _logger;

        public const string NotificationType = "attendance-late";

        public EmployeeLateNotificationService(
            AttendanceDbContext db,
            IAttendancePolicyService policyService,
            IEmailSender emailSender,
            ILogger<EmployeeLateNotificationService> logger)
        {
            _db = db;
            _policyService = policyService;
            _emailSender = emailSender;
            _logger = logger;
        }

        public Task<int> ProcessTodayLateArrivalsAsync(CancellationToken ct = default)
            => ProcessDailyLateArrivalsAsync(DateTime.Today, ct);

        public async Task<int> ProcessDailyLateArrivalsAsync(DateTime date, CancellationToken ct = default)
        {
            var day = date.Date;
            var policy = await _policyService.GetAsync(ct);

            var graceCutoff = policy.OfficeStartTime.Add(TimeSpan.FromMinutes(policy.GraceMinutes));

            // Find all first check-ins for the day
            var firstPunches = await _db.AttendanceLogs
                .Where(a => a.EmployeeId != null
                            && a.AttendanceTime >= day
                            && a.AttendanceTime < day.AddDays(1))
                .GroupBy(a => a.EmployeeId!.Value)
                .Select(g => new
                {
                    EmployeeId = g.Key,
                    FirstCheckIn = g.Min(x => x.AttendanceTime)
                })
                .ToListAsync(ct);

            if (firstPunches.Count == 0)
            {
                _logger.LogDebug("No attendance punches found for {Date}", day.ToString("yyyy-MM-dd"));
                return 0;
            }

            var notifiedCount = 0;

            foreach (var punch in firstPunches)
            {
                ct.ThrowIfCancellationRequested();

                var punchTimeOfDay = punch.FirstCheckIn.TimeOfDay;

                // Check if late (past grace cutoff)
                if (punchTimeOfDay <= graceCutoff)
                {
                    continue; // On time
                }

                // Calculate minutes late past office start time
                var minutesLate = (int)Math.Ceiling((punchTimeOfDay - policy.OfficeStartTime).TotalMinutes);
                if (minutesLate <= policy.GraceMinutes)
                {
                    continue;
                }

                // Check if employee exists and is configured to check late
                var employee = await _db.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == punch.EmployeeId && e.IsActive, ct);

                if (employee is null || !employee.CheckAttendance || !employee.CheckLate)
                {
                    continue;
                }

                // Deduplication check: check if already notified today for late arrival
                var deduplicationKey = $"/attendance?date={day:yyyy-MM-dd}&emp={employee.EmployeeId}";
                var alreadyNotified = await _db.Notifications
                    .AnyAsync(n => n.Type == NotificationType
                                && n.LinkPath == deduplicationKey
                                && n.CreatedDate >= day
                                && n.CreatedDate < day.AddDays(1), ct);

                if (alreadyNotified)
                {
                    continue;
                }

                await SendLateNotificationToEmployeeAsync(
                    employee.EmployeeId,
                    day,
                    punchTimeOfDay,
                    minutesLate,
                    ct);

                notifiedCount++;
            }

            if (notifiedCount > 0)
            {
                _logger.LogInformation(
                    "Processed late arrival notifications for {Count} employee(s) on {Date}",
                    notifiedCount, day.ToString("yyyy-MM-dd"));
            }

            return notifiedCount;
        }

        public async Task SendLateNotificationToEmployeeAsync(
            int employeeId,
            DateTime attendanceDate,
            TimeSpan checkInTime,
            int minutesLate,
            CancellationToken ct = default)
        {
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId, ct);

            if (employee is null)
            {
                _logger.LogWarning("Cannot notify employee {Id}: record not found", employeeId);
                return;
            }

            var policy = await _policyService.GetAsync(ct);
            var checkInFormatted = DateTime.Today.Add(checkInTime).ToString("hh:mm tt");
            var startFormatted = DateTime.Today.Add(policy.OfficeStartTime).ToString("hh:mm tt");
            var dayFormatted = attendanceDate.ToString("dddd, MMM dd, yyyy");

            var message = $"Late Arrival Notice: Check-in was at {checkInFormatted} ({minutesLate} min late). Office start is {startFormatted} (Grace: {policy.GraceMinutes}m).";
            var deduplicationKey = $"/attendance?date={attendanceDate:yyyy-MM-dd}&emp={employeeId}";

            // 1. In-App Notification (Bell icon)
            var linkedUser = await _db.ApiUsers
                .FirstOrDefaultAsync(u => u.EmployeeId == employeeId && u.IsActive, ct);

            _db.Notifications.Add(new Notification
            {
                RecipientUserId = linkedUser?.ApiUserId,
                RecipientRole = linkedUser is null ? "HR" : null, // If employee has no login account, notify HR
                Message = linkedUser is null ? $"{employee.EmployeeName}: {message}" : message,
                LinkPath = deduplicationKey,
                Type = NotificationType,
                IsRead = false,
                CreatedDate = DateTime.Now
            });

            await _db.SaveChangesAsync(ct);

            // 2. Email Delivery (if employee has email)
            var recipientEmail = employee.Email ?? linkedUser?.Email;
            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                var subject = $"Late Arrival Notice - {dayFormatted}";
                var bodyHtml = BuildLateEmailHtml(
                    employee.EmployeeName,
                    dayFormatted,
                    checkInFormatted,
                    startFormatted,
                    policy.GraceMinutes,
                    minutesLate,
                    policy.RequireApprovalForLate && minutesLate > policy.ApprovalRequiredAfterMinutes);

                try
                {
                    await _emailSender.SendEmailAsync(recipientEmail, subject, bodyHtml, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send late notification email to {Email}", recipientEmail);
                    // Do not rethrow here to allow subsequent processing, or Hangfire retries can wrap this
                }
            }
        }

        private static string BuildLateEmailHtml(
            string employeeName,
            string dateStr,
            string checkInTime,
            string officeStartTime,
            int graceMinutes,
            int minutesLate,
            bool needsApproval)
        {
            var approvalNotice = needsApproval
                ? @"<div style='margin-top:16px;padding:12px 16px;background:#fef2f2;border-left:4px solid #ef4444;border-radius:4px;color:#991b1b;'>
                        <strong>Approval Required:</strong> Because arrival exceeded the late cut-off threshold, this attendance record has been flagged for administrative review.
                   </div>"
                : string.Empty;

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'/>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f3f4f6; margin: 0; padding: 24px; color: #1f2937; }}
    .container {{ max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); }}
    .header {{ background: #1e293b; color: #ffffff; padding: 24px; text-align: center; }}
    .header h2 {{ margin: 0; font-size: 20px; font-weight: 600; letter-spacing: 0.5px; }}
    .content {{ padding: 28px; line-height: 1.6; }}
    .badge {{ display: inline-block; background: #fee2e2; color: #dc2626; padding: 4px 10px; border-radius: 9999px; font-weight: 600; font-size: 13px; margin-bottom: 12px; }}
    .table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}
    .table td {{ padding: 10px 14px; border-bottom: 1px solid #e5e7eb; font-size: 14px; }}
    .table td.label {{ color: #6b7280; font-weight: 500; width: 45%; }}
    .table td.val {{ color: #111827; font-weight: 600; }}
    .footer {{ background: #f9fafb; padding: 16px; text-align: center; font-size: 12px; color: #9ca3af; border-top: 1px solid #e5e7eb; }}
  </style>
</head>
<body>
  <div class='container'>
    <div class='header'>
      <h2>ZKAttendance Notification</h2>
    </div>
    <div class='content'>
      <span class='badge'>Late Arrival Recorded</span>
      <p>Dear <strong>{employeeName}</strong>,</p>
      <p>Your first attendance check-in for <strong>{dateStr}</strong> was recorded past the scheduled start and grace window.</p>
      
      <table class='table'>
        <tr>
          <td class='label'>Scheduled Office Start</td>
          <td class='val'>{officeStartTime}</td>
        </tr>
        <tr>
          <td class='label'>Grace Period</td>
          <td class='val'>{graceMinutes} minutes</td>
        </tr>
        <tr>
          <td class='label'>Recorded Check-In</td>
          <td class='val' style='color:#dc2626;'>{checkInTime}</td>
        </tr>
        <tr>
          <td class='label'>Minutes Late</td>
          <td class='val' style='color:#dc2626;'>{minutesLate} minutes</td>
        </tr>
      </table>

      {approvalNotice}

      <p style='margin-top:24px; font-size:13px; color:#6b7280;'>
        If you believe this punch was recorded in error, please contact your department supervisor or HR administrator.
      </p>
    </div>
    <div class='footer'>
      This is an automated message from the ZKAttendance System. Please do not reply directly to this email.
    </div>
  </div>
</body>
</html>";
        }
    }
}
