using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>How a day's attendance stands with the admin.</summary>
    public enum AttendanceApprovalStatus
    {
        /// <summary>Arrived inside the grace window. No one needs to look at it.</summary>
        AutoApproved = 0,

        /// <summary>Arrived late enough to need a decision.</summary>
        Pending = 1,

        /// <summary>An admin accepted the late arrival.</summary>
        Approved = 2,

        /// <summary>An admin refused it. The day does not count as attended.</summary>
        Rejected = 3
    }

    /// <summary>
    /// One decision per employee per day.
    ///
    /// Why a separate table rather than a column on AttendanceLog: a person can
    /// scan many times in a day, but there is only ever one decision about
    /// whether that day counts. Hanging the decision off a single punch would
    /// mean picking which punch owns it, and re-picking every time an earlier
    /// scan arrives late from a device.
    ///
    /// Rows are created only when a decision is actually needed. A day nobody
    /// has to look at leaves no row behind.
    /// </summary>
    [Table("AttendanceApprovals")]
    public class AttendanceApproval
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ApprovalId { get; set; }

        [Required]
        public int EmployeeId { get; set; }

        /// <summary>Date only. The time lives in FirstCheckIn.</summary>
        [Required]
        [Column(TypeName = "date")]
        public DateTime AttendanceDate { get; set; }

        [Required]
        public AttendanceApprovalStatus Status { get; set; } = AttendanceApprovalStatus.Pending;

        /// <summary>The scan that triggered this, kept so the record still reads correctly if logs are purged.</summary>
        public DateTime? FirstCheckIn { get; set; }

        /// <summary>Minutes past the office start time, after grace.</summary>
        public int MinutesLate { get; set; }

        [StringLength(100)]
        public string? DecidedBy { get; set; }

        public DateTime? DecidedDate { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public virtual Employee? Employee { get; set; }
    }
}
