using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    [Table("DeviceErrors")]
    public class DeviceError
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long ErrorId { get; set; }

        [Required]
        public int DeviceId { get; set; }

        [Required]
        public int BranchId { get; set; }

        [Required]
        [StringLength(500)]
        public string ErrorMessage { get; set; } = string.Empty;

        [StringLength(50)]
        public string? ErrorCode { get; set; }

        [Required]
        public DateTime ErrorDateTime { get; set; } = DateTime.Now;

        public bool IsResolved { get; set; } = false;

        public DateTime? ResolvedDateTime { get; set; }

        [StringLength(100)]
        public string? ResolvedBy { get; set; }

        [StringLength(1000)]
        public string? Resolution { get; set; }

        [Required]
        [StringLength(20)]
        public string Severity { get; set; } = "Medium";

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("DeviceId")]
        public virtual Device? Device { get; set; }

        [ForeignKey("BranchId")]
        public virtual Branch? Branch { get; set; }
    }
}
