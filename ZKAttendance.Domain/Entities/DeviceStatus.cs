using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    [Table("DeviceStatuses")]  
    public class DeviceStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long StatusId { get; set; }

        [Required]  
        public int DeviceId { get; set; }

        [Required]  
        public int BranchId { get; set; }

        public bool IsOnline { get; set; } = false;

        [Required]
        public DateTime StatusTime { get; set; } = DateTime.Now;

        public DateTime? DeviceTime { get; set; }

        public DateTime? LastConnectionTime { get; set; }

        [Required]
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;

        [MaxLength(200)]
        public string? StatusMessage { get; set; }

        public int? UserCount { get; set; }

        public int? LogCount { get; set; }

        public int? FaceCount { get; set; }

        [MaxLength(100)]  
        public string? FirmwareVersion { get; set; }

        public long? FreeSpace { get; set; }

        public long? TotalSpace { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        [MaxLength(100)]
        public string? DeviceModel { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("DeviceId")]  
        public virtual Device? Device { get; set; }

        [ForeignKey("BranchId")]  
        public virtual Branch? Branch { get; set; }
    }
}
