using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZKAttendance.Application.Abstractions
{
    /// <summary>
    /// The office-hours rules, all editable from Settings.
    ///
    /// Times are local. Everything is stored in SystemSettings as key/value, so
    /// changing a rule needs no deployment and no edit to appsettings.json.
    /// </summary>
    public class AttendancePolicy
    {
        /// <summary>When the working day starts. Default 10:00.</summary>
        public TimeSpan OfficeStartTime { get; set; } = new(10, 0, 0);

        /// <summary>When it ends. Also the point after which a no-show counts as absent.</summary>
        public TimeSpan OfficeEndTime { get; set; } = new(18, 0, 0);

        /// <summary>Arrive within this many minutes of the start and the day is approved on its own.</summary>
        public int GraceMinutes { get; set; } = 15;

        /// <summary>
        /// Minutes after the start time beyond which an arrival needs an admin
        /// decision. Default 60, i.e. 11:00 for a 10:00 start.
        /// </summary>
        public int ApprovalRequiredAfterMinutes { get; set; } = 60;

        /// <summary>
        /// Whether arrivals between grace and the approval cut-off also need a
        /// decision. Off by default: they are marked late but still count.
        /// </summary>
        public bool RequireApprovalForLate { get; set; } = false;

        /// <summary>Minutes after the office end before a no-show is recorded absent.</summary>
        public int CloseGraceMinutes { get; set; } = 30;

        /// <summary>Under this many hours present, the day is a half day.</summary>
        public double HalfDayUnderHours { get; set; } = 4.0;

        // ── derived ──────────────────────────────────────────────────

        /// <summary>Latest on-time arrival. 10:15 with the defaults.</summary>
        public TimeSpan GraceUntil => OfficeStartTime.Add(TimeSpan.FromMinutes(GraceMinutes));

        /// <summary>Arrive after this and an admin has to decide. 11:00 with the defaults.</summary>
        public TimeSpan ApprovalRequiredAfter =>
            OfficeStartTime.Add(TimeSpan.FromMinutes(ApprovalRequiredAfterMinutes));

        /// <summary>When a working day closes for absence purposes.</summary>
        public TimeSpan DayClosesAt => OfficeEndTime.Add(TimeSpan.FromMinutes(CloseGraceMinutes));

        /// <summary>
        /// Which bucket a check-in falls into.
        ///
        ///   at or before 10:15  OnTime          counts, nobody looks at it
        ///   10:15 to 11:00      Late            counts, flagged (unless RequireApprovalForLate)
        ///   after 11:00         NeedsApproval   does not count until an admin says so
        /// </summary>
        public ArrivalOutcome Classify(TimeSpan checkIn)
        {
            if (checkIn <= GraceUntil) return ArrivalOutcome.OnTime;
            if (checkIn > ApprovalRequiredAfter) return ArrivalOutcome.NeedsApproval;
            return RequireApprovalForLate ? ArrivalOutcome.NeedsApproval : ArrivalOutcome.Late;
        }

        /// <summary>Minutes past the end of grace. Zero when on time.</summary>
        public int MinutesLate(TimeSpan checkIn) =>
            checkIn <= GraceUntil ? 0 : (int)(checkIn - GraceUntil).TotalMinutes;
    }

    public enum ArrivalOutcome
    {
        OnTime,
        Late,
        NeedsApproval
    }

    /// <summary>Reads and writes the policy, and records approval decisions.</summary>
    public interface IAttendancePolicyService
    {
        /// <summary>Current rules. Cached; call is cheap enough for per-request use.</summary>
        Task<AttendancePolicy> GetAsync(CancellationToken ct = default);

        /// <summary>Save new rules and drop the cache.</summary>
        Task<AttendancePolicy> SaveAsync(AttendancePolicy policy, string? changedBy = null, CancellationToken ct = default);

        /// <summary>
        /// Make sure a day that needs a decision has a row waiting, and that a
        /// day that does not is marked auto-approved. Called after a sync so the
        /// approval queue reflects what the devices actually reported.
        /// </summary>
        Task<int> EvaluateDayAsync(DateTime date, CancellationToken ct = default);
    }
}
