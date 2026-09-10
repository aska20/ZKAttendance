using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Services.Devices;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Background job triggers and diagnostics powered by Hangfire.
    /// </summary>
    [Route("api/Jobs")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Background Jobs")]
    [Authorize(Roles = Roles.Management)]
    public class HangfireJobsApiController : ControllerBase
    {
        private readonly IBackgroundJobClient _jobClient;

        public HangfireJobsApiController(IBackgroundJobClient jobClient)
        {
            _jobClient = jobClient;
        }

        /// <summary>
        /// Manually triggers a device sync round across all active biometric terminals in the background.
        /// </summary>
        [HttpPost("trigger-sync")]
        [ProducesResponseType(202)]
        public IActionResult TriggerSync()
        {
            var jobId = _jobClient.Enqueue<IAttendanceSyncService>(
                syncService => syncService.SyncAllDevicesAsync(CancellationToken.None));

            return Accepted(new { message = "Device sync round scheduled in background", jobId });
        }

        /// <summary>
        /// Manually triggers the late arrival evaluation and notification job for today's punches.
        /// </summary>
        [HttpPost("trigger-late-check")]
        [ProducesResponseType(202)]
        public IActionResult TriggerLateCheck([FromQuery] DateTime? date = null)
        {
            var targetDate = (date ?? DateTime.Today).Date;

            var jobId = _jobClient.Enqueue<IEmployeeLateNotificationService>(
                notificationService => notificationService.ProcessDailyLateArrivalsAsync(targetDate, CancellationToken.None));

            return Accepted(new { message = "Late arrival evaluation scheduled in background", date = targetDate.ToString("yyyy-MM-dd"), jobId });
        }

        /// <summary>
        /// Sends a test late notification to a specific employee to verify in-app notification and email formatting.
        /// </summary>
        [HttpPost("test-late-notification")]
        [ProducesResponseType(202)]
        public IActionResult TestLateNotification(
            [FromQuery] int employeeId,
            [FromQuery] int minutesLate = 30)
        {
            var arrivalTime = new TimeSpan(10, minutesLate, 0); // e.g. 10:30 AM
            var today = DateTime.Today;

            var jobId = _jobClient.Enqueue<IEmployeeLateNotificationService>(
                notificationService => notificationService.SendLateNotificationToEmployeeAsync(
                    employeeId,
                    today,
                    arrivalTime,
                    minutesLate,
                    CancellationToken.None));

            return Accepted(new
            {
                message = "Test late notification enqueued",
                employeeId,
                minutesLate,
                simulatedCheckIn = arrivalTime.ToString(@"hh\:mm"),
                jobId
            });
        }

        /// <summary>
        /// Returns all configured recurring jobs and their current schedule.
        /// </summary>
        [HttpGet("recurring")]
        [ProducesResponseType(200)]
        public IActionResult GetRecurringJobs()
        {
            using var connection = JobStorage.Current.GetConnection();
            var recurringJobs = connection.GetRecurringJobs();

            var list = recurringJobs.Select(j => new
            {
                j.Id,
                j.Cron,
                j.Queue,
                j.NextExecution,
                j.LastExecution,
                j.LastJobState,
                j.CreatedAt
            });

            return Ok(list);
        }
    }
}
