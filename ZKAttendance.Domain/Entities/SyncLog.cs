using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    [Table("SyncLogs")]  
    public class SyncLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SyncId { get; set; }

        [Required]  
        public int DeviceId { get; set; }

        [Required]  
        public int BranchId { get; set; }

        [Required]
        public DateTime StartTime { get; set; } = DateTime.Now;

        public DateTime? EndTime { get; set; }

        // Duration is calculated, not stored
        [NotMapped]
        public TimeSpan? Duration
        {
            get
            {
                if (EndTime.HasValue)
                    return EndTime.Value - StartTime;
                return null;
            }
        }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";

        public int RecordCount { get; set; } = 0;

        public int NewRecordCount { get; set; } = 0;

        public int DuplicateCount { get; set; } = 0;

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        [MaxLength(50)]
        public string? ErrorCode { get; set; }

        [MaxLength(100)]
        public string? ServerName { get; set; } = Environment.MachineName;

        public int RetryAttempt { get; set; } = 0;

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("DeviceId")]  
        public virtual Device? Device { get; set; }

        [ForeignKey("BranchId")]  
        public virtual Branch? Branch { get; set; }
    }
}
