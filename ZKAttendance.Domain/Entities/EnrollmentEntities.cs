using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    public enum DeviceRole
    {
        /// <summary>
        /// Verifies people and records punches. Never enrolled on directly -
        /// its users arrive by being written from the application.
        /// </summary>
        Slave = 0,

        /// <summary>
        /// The single enrolment point. Fingerprints are captured here and
        /// pulled into the database, then propagated outward. Also records
        /// attendance like any other device.
        /// </summary>
        Master = 1
    }

    public enum EnrollmentStatus
    {
        /// <summary>Found on the master, waiting for HR to fill in the details.</summary>
        AwaitingReview = 0,

        /// <summary>Turned into an Employee and queued for propagation.</summary>
        Approved = 1,

        /// <summary>A test enrolment or a mistake. Kept on record so it is not re-discovered.</summary>
        Rejected = 2
    }

    /// <summary>
    /// A fingerprint template captured from the master device and cached here.
    ///
    /// WHY STORE IT AT ALL
    /// -------------------
    /// Without a stored copy, adding a fourth device would mean calling every
    /// employee back to press their finger again. With it, provisioning a new
    /// device is a background job that finishes in minutes and nobody is
    /// disturbed. It is also the only protection against the master device
    /// dying or having its memory cleared.
    /// </summary>
    [Table("FingerprintTemplates")]
    public class FingerprintTemplate
    {
        [Key]
        public int TemplateId { get; set; }

        [Required]
        public int EmployeeId { get; set; }

        /// <summary>0-9, which finger this is. A person may enrol several.</summary>
        public int FingerIndex { get; set; }

        /// <summary>Raw template bytes exactly as the SDK returned them.</summary>
        [Required]
        public byte[] TemplateData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 9 or 10. ZKTeco firmware families use incompatible template formats,
        /// so a template captured from a v10 master cannot be written to a v9
        /// slave. Storing the version lets provisioning fail with a clear
        /// message instead of writing a template that silently never matches -
        /// a failure invisible until somebody stands at the door.
        /// </summary>
        public int TemplateFormatVersion { get; set; } = 10;

        /// <summary>Which device this came from - normally the master.</summary>
        public int SourceDeviceId { get; set; }

        public DateTime CapturedDate { get; set; } = DateTime.Now;

        [ForeignKey(nameof(EmployeeId))]
        public virtual Employee? Employee { get; set; }
    }

    /// <summary>
    /// A user found on the master device who has no Employee record yet.
    ///
    /// This is the pull-based enrolment flow. HR walks the new joiner to the
    /// master machine and enrols their finger there, because that is the only
    /// place a fingerprint can physically be captured. The application then
    /// discovers them on the next pull and parks them here until somebody fills
    /// in the name, department and shift.
    ///
    /// Nothing becomes an Employee automatically: a technician testing the
    /// sensor would otherwise silently become a payroll record.
    /// </summary>
    [Table("PendingEnrollments")]
    public class PendingEnrollment
    {
        [Key]
        public int PendingEnrollmentId { get; set; }

        [Required]
        public int DeviceId { get; set; }

        /// <summary>The enrol number the master device assigned.</summary>
        [Required, StringLength(12)]
        public string DeviceUserId { get; set; } = string.Empty;

        /// <summary>Whatever was typed on the device keypad - often blank or rough.</summary>
        [StringLength(100)]
        public string? NameOnDevice { get; set; }

        public int FingerCount { get; set; }
        public int Privilege { get; set; }

        public DateTime DiscoveredDate { get; set; } = DateTime.Now;

        public EnrollmentStatus Status { get; set; } = EnrollmentStatus.AwaitingReview;

        /// <summary>Set once approved, linking to the employee that was created.</summary>
        public int? ApprovedEmployeeId { get; set; }

        public DateTime? ReviewedDate { get; set; }

        [StringLength(100)]
        public string? ReviewedBy { get; set; }

        [StringLength(400)]
        public string? RejectionReason { get; set; }

        [ForeignKey(nameof(DeviceId))]
        public virtual Device? Device { get; set; }
    }
}
