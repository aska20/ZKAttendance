using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// A tenant. Scoping is deliberately anchored at the Branch: devices,
    /// attendance, employee assignments and holidays all already hang off a
    /// branch, so one column here scopes the whole tree without touching
    /// fifteen tables.
    ///
    /// NOT YET ENFORCED. The column and the table exist so the data model is
    /// ready, but no global query filter is switched on, because silently
    /// filtering every existing query is exactly the kind of change that breaks
    /// a working system. See docs/SECURITY-REVIEW.md for what enforcement needs.
    /// </summary>
    [Table("Companies")]
    public class Company
    {
        [Key]
        [DatabaseGeneratedAttribute(DatabaseGeneratedOption.Identity)]
        public int CompanyId { get; set; }

        [Required, StringLength(150)]
        public string CompanyName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? CompanyCode { get; set; }

        [StringLength(30)]
        public string? RegistrationNumber { get; set; }

        [StringLength(30)]
        public string? PanNumber { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }

        public virtual ICollection<Branch> Branches { get; set; } = new List<Branch>();
    }

    /// <summary>What kind of change an audit row records.</summary>
    public enum AuditAction
    {
        Create = 0,
        Update = 1,
        Delete = 2,
        Approve = 3,
        Reject = 4,
        Login = 5,
        LoginFailed = 6,
        SettingsChange = 7,
        DeviceCommand = 8,
        Export = 9
    }

    /// <summary>
    /// Who changed what, when, and from where.
    ///
    /// Attendance data decides pay, so "the system says you were absent" has to
    /// be answerable with a record rather than a shrug. This table is
    /// append-only by convention: nothing in the application updates or deletes
    /// a row, and the SQL script grants no UPDATE or DELETE on it.
    ///
    /// OldValues / NewValues hold JSON rather than columns per field, because
    /// the alternative is a shadow copy of every table.
    /// </summary>
    [Table("AuditLogs")]
    public class AuditLog
    {
        [Key]
        [DatabaseGeneratedAttribute(DatabaseGeneratedOption.Identity)]
        public long AuditId { get; set; }

        /// <summary>Table or logical area, e.g. "AttendanceApprovals".</summary>
        [Required, StringLength(100)]
        public string EntityName { get; set; } = string.Empty;

        /// <summary>Primary key of the affected row, as text so any key type fits.</summary>
        [StringLength(50)]
        public string? EntityId { get; set; }

        [Required]
        public AuditAction Action { get; set; }

        /// <summary>Username. Not a FK: the record must survive the user being deleted.</summary>
        [StringLength(100)]
        public string? PerformedBy { get; set; }

        public int? PerformedByUserId { get; set; }

        [StringLength(45)]
        public string? IpAddress { get; set; }

        [StringLength(300)]
        public string? UserAgent { get; set; }

        /// <summary>JSON. Null for a create.</summary>
        public string? OldValues { get; set; }

        /// <summary>JSON. Null for a delete.</summary>
        public string? NewValues { get; set; }

        [StringLength(500)]
        public string? Summary { get; set; }

        [Required]
        public DateTime PerformedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Cold storage for punches older than the retention window.
    ///
    /// AttendanceLogs grows without bound: 200 employees scanning four times a
    /// day is roughly 290,000 rows a year, and the attendance grid scans it on
    /// every page load. Moving old rows here keeps the hot table small while
    /// keeping the history, which payroll disputes and audits need.
    ///
    /// Deliberately NOT a partitioned table. Partitioning needs Enterprise
    /// edition on older SQL Server versions and a partition function tied to a
    /// filegroup layout, which is a DBA decision rather than an application
    /// one. An archive table achieves the same practical result on any edition.
    /// </summary>
    [Table("AttendanceLogsArchive")]
    public class AttendanceLogArchive
    {
        [Key]
        public long LogId { get; set; }

        [Required, StringLength(12)]
        [Column(TypeName = "varchar(12)")]
        public string BiometricUserId { get; set; } = string.Empty;

        public int? EmployeeId { get; set; }
        public int DeviceId { get; set; }
        public int BranchId { get; set; }

        [Required]
        public DateTime AttendanceTime { get; set; }

        [StringLength(50)]
        public string? AttendanceType { get; set; }

        [StringLength(50)]
        public string? VerifyMethod { get; set; }

        public int? WorkCode { get; set; }
        public bool IsManual { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        /// <summary>When the row was moved out of the hot table.</summary>
        public DateTime ArchivedDate { get; set; } = DateTime.Now;
    }
}
