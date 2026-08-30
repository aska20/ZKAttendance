using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    [Table("Holidays")]
    public class Holiday
    {
        [Key]
        public int HolidayId { get; set; }

        [Required]
        [StringLength(100)]
        public string HolidayName { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "date")]
        public DateTime HolidayDate { get; set; }

        public int DurationDays { get; set; } = 1;

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(20)]
        public string? HolidayType { get; set; }

        /// <summary>True for holidays that repeat every year.</summary>
        public bool IsRecurring { get; set; } = false;

        /// <summary>True for the recurring weekly off day.</summary>
        public bool IsWeeklyRecurring { get; set; } = false;

        /// <summary>Day of week for the weekly off (0 = Sunday, 6 = Saturday).</summary>
        [Column(TypeName = "int")]
        public DayOfWeek? RecurringDayOfWeek { get; set; }

        /// <summary>Applies to one branch, or all branches when null.</summary>
        public int? BranchId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }

        // Navigation Properties
        [ForeignKey("BranchId")]
        public virtual Branch? Branch { get; set; }
    }

}
