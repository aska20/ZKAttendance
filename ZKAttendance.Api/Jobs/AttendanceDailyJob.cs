using Hangfire;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Api.Jobs
{
    /// <summary>
    /// The scheduled end-of-day run.
    ///
    /// Deliberately thin. Every line of attendance logic lives in
    /// IDailyAttendanceService, which the admin buttons call too, so the
    /// automatic and manual runs cannot drift apart. This class only exists to
    /// give Hangfire something to serialise a job reference to, and to decide
    /// which DATE to process.
    /// </summary>
    public class AttendanceDailyJob
    {
        private readonly IDailyAttendanceService _service;
        private readonly ILogger<AttendanceDailyJob> _logger;

        public AttendanceDailyJob(
            IDailyAttendanceService service,
            ILogger<AttendanceDailyJob> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Process today and email everyone.
        ///
        /// Two retries, not Hangfire's default of ten. This job sends email in
        /// a loop; ten retries after a partial failure would send duplicates to
        /// everyone who already received one. Per-recipient failures are
        /// already swallowed inside the service, so a retry only happens when
        /// something larger breaks, such as the database being unreachable.
        /// </summary>
        [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public async Task RunEndOfDayAsync(CancellationToken ct = default)
        {
            // Resolved at run time, not when the schedule was registered.
            // Passing DateTime.Today into RecurringJob.AddOrUpdate would freeze
            // the date at whatever it was the day the app last started, and the
            // job would re-process that same day for ever.
            var today = DateTime.Today;

            _logger.LogInformation("Scheduled end-of-day job starting for {Date}", today.ToString("dd/MM/yyyy"));

            var report = await _service.RunEndOfDayAsync(today, sendEmails: true, ct);

            _logger.LogInformation(
                "Scheduled end-of-day job finished for {Date}: {Present} present, {Late} late, {Partial} partial, {Absent} absent of {Total}",
                today.ToString("dd/MM/yyyy"),
                report.PresentCount, report.LateCount, report.PartialCount,
                report.AbsentCount, report.TotalEmployees);
        }

        /// <summary>
        /// Recalculate a specific day without emailing. Useful from the
        /// dashboard when a device was offline and its punches arrived late.
        /// </summary>
        [AutomaticRetry(Attempts = 1)]
        public async Task ReprocessAsync(DateTime date, CancellationToken ct = default)
        {
            _logger.LogInformation("Reprocessing {Date}", date.ToString("dd/MM/yyyy"));
            await _service.RunEndOfDayAsync(date.Date, sendEmails: false, ct);
        }
    }
}
