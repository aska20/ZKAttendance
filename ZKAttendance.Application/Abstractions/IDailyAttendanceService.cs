using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZKAttendance.Application.Abstractions
{
    /// <summary>
    /// The four states a day can be in for one person.
    ///
    /// Distinct from AttendanceApprovalStatus, which answers a different
    /// question: this is "what happened", that is "does an admin accept it".
    /// A day can be Late AND Approved.
    /// </summary>
    public enum DailyAttendanceStatus
    {
        /// <summary>Checked in within grace, and checked out.</summary>
        Present = 0,

        /// <summary>Checked in after grace.</summary>
        Late = 1,

        /// <summary>Scanned, but only once. Usually a forgotten check-out.</summary>
        Partial = 2,

        /// <summary>No scan at all on a working day.</summary>
        Absent = 3,

        /// <summary>Weekly off or a declared holiday. Not counted either way.</summary>
        Holiday = 4,

        /// <summary>Date is before this person's hire date.</summary>
        NotJoined = 5
    }

    /// <summary>One employee's day.</summary>
    public class DailyAttendanceRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>The ZKTeco enrol number this person is known by.</summary>
        public string? DeviceUserId { get; set; }

        public string? DepartmentName { get; set; }
        public string? Email { get; set; }

        public DateTime Date { get; set; }
        public string DateBs { get; set; } = string.Empty;

        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }

        /// <summary>Every scan, in order. Two is the common case; four with a lunch break.</summary>
        public List<DateTime> Punches { get; set; } = new();

        public DailyAttendanceStatus Status { get; set; }
        public int MinutesLate { get; set; }
        public double WorkedHours { get; set; }

        /// <summary>Which device recorded the first scan.</summary>
        public string? DeviceName { get; set; }

        /// <summary>Set when the arrival was late enough to need an admin decision.</summary>
        public bool NeedsApproval { get; set; }
        public string? ApprovalStatus { get; set; }

        public string StatusText => Status switch
        {
            DailyAttendanceStatus.Present => "Present",
            DailyAttendanceStatus.Late => "Late",
            DailyAttendanceStatus.Partial => "Partial",
            DailyAttendanceStatus.Absent => "Absent",
            DailyAttendanceStatus.Holiday => "Holiday",
            DailyAttendanceStatus.NotJoined => "Not joined",
            _ => "Unknown"
        };

        /// <summary>Single letter for the cross-tab grid.</summary>
        public string Mark => Status switch
        {
            DailyAttendanceStatus.Present => "P",
            DailyAttendanceStatus.Late => "L",
            DailyAttendanceStatus.Partial => "PT",
            DailyAttendanceStatus.Absent => "A",
            DailyAttendanceStatus.Holiday => "H",
            DailyAttendanceStatus.NotJoined => "-",
            _ => "?"
        };
    }

    /// <summary>Everyone's day, plus the totals the admin summary needs.</summary>
    public class DailyAttendanceReport
    {
        public DateTime Date { get; set; }
        public string DateBs { get; set; } = string.Empty;
        public bool IsHoliday { get; set; }
        public string? HolidayName { get; set; }

        public List<DailyAttendanceRow> Rows { get; set; } = new();

        public int TotalEmployees => Rows.Count;
        public int PresentCount { get; set; }
        public int LateCount { get; set; }
        public int PartialCount { get; set; }
        public int AbsentCount { get; set; }

        /// <summary>Enrol numbers seen on a device that map to nobody here.</summary>
        public List<string> UnmappedDeviceIds { get; set; } = new();
    }

    /// <summary>One row of the month grid: an employee and a mark per day.</summary>
    public class MonthlyAttendanceRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string? DepartmentName { get; set; }
        public string? DeviceUserId { get; set; }

        /// <summary>Keyed by yyyy-MM-dd.</summary>
        public Dictionary<string, string> Marks { get; set; } = new();

        public int TotalPresent { get; set; }
        public int TotalLate { get; set; }
        public int TotalPartial { get; set; }
        public int TotalAbsent { get; set; }
        public double TotalHours { get; set; }
    }

    public class MonthlyAttendanceReport
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string FromBs { get; set; } = string.Empty;
        public string ToBs { get; set; } = string.Empty;

        /// <summary>The date columns, in order, as yyyy-MM-dd.</summary>
        public List<string> Dates { get; set; } = new();

        /// <summary>Which of those are non-working, so the grid can grey them.</summary>
        public HashSet<string> HolidayDates { get; set; } = new();

        public List<MonthlyAttendanceRow> Rows { get; set; } = new();
        public int WorkingDays { get; set; }
    }

    /// <summary>What an email run did.</summary>
    public class EmailDispatchResult
    {
        public int Sent { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// The end-of-day processing, used by BOTH the scheduled job and the admin
    /// buttons.
    ///
    /// Keeping this free of any scheduling concern is the whole point: Hangfire
    /// only decides WHEN, this decides WHAT. Duplicating the logic in a job
    /// class is how the automatic and manual runs end up disagreeing.
    /// </summary>
    public interface IDailyAttendanceService
    {
        /// <summary>
        /// Every ACTIVE employee's day, whether or not they scanned.
        ///
        /// This is the part that is easy to get wrong: querying attendance
        /// records alone silently omits the people who never turned up, who are
        /// precisely the ones the report exists to surface. The employee list
        /// drives the result and attendance is joined onto it, never the
        /// reverse.
        /// </summary>
        Task<DailyAttendanceReport> BuildDailyReportAsync(
            DateTime date, int? departmentId = null, CancellationToken ct = default);

        /// <summary>Cross-tab: employees down, dates across.</summary>
        Task<MonthlyAttendanceReport> BuildMonthlyReportAsync(
            DateTime from, DateTime to, int? departmentId = null, CancellationToken ct = default);

        /// <summary>
        /// Send each employee their own attendance for the day. One email per
        /// person, never a combined list, since the contents are personal.
        /// </summary>
        Task<EmailDispatchResult> SendEmployeeEmailsAsync(
            DateTime date, CancellationToken ct = default);

        /// <summary>Send the admin the whole-day summary.</summary>
        Task<EmailDispatchResult> SendAdminSummaryAsync(
            DateTime date, CancellationToken ct = default);

        /// <summary>
        /// The full end-of-day run: refresh approvals, email everyone, email
        /// the admin. This is the single entry point the scheduled job calls,
        /// and the same one the "Process today" button calls.
        /// </summary>
        Task<DailyAttendanceReport> RunEndOfDayAsync(
            DateTime date, bool sendEmails = true, CancellationToken ct = default);
    }

    /// <summary>Minimal mail abstraction so the service never touches SMTP directly.</summary>
    public interface IEmailSender
    {
        /// <summary>False when email is not configured, rather than throwing.</summary>
        Task<bool> IsConfiguredAsync(CancellationToken ct = default);

        Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    }
}
