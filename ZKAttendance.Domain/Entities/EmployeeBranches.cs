// Models/EmployeeBranch.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// Join table linking employees to branches (many-to-many)
    /// Allows one employee to work at several branches
    /// </summary>
    [Table("EmployeeBranches")]
    public class EmployeeBranch
    {
        /// <summary>
        /// Primary key
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EmployeeBranchId { get; set; }

        /// <summary>
        /// Employee ID (foreign key)
        /// </summary>
        [Required(ErrorMessage = "Employee ID is required")]
        public int EmployeeId { get; set; }

        /// <summary>
        /// Branch ID (foreign key)
        /// </summary>
        [Required(ErrorMessage = "Branch ID is required")]
        public int BranchId { get; set; }

        /// <summary>
        /// Date the employee was assigned to the branch
        /// </summary>
        [Required]
        public DateTime AssignedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Is the assignment active?
        /// false = the employee no longer works at this branch
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Date the assignment was ended, if any
        /// </summary>
        public DateTime? DeactivatedDate { get; set; }

        /// <summary>
        /// Optional notes
        /// Example: "Transferred from Kathmandu branch"
        /// </summary>
        [MaxLength(500)]
        public string? Notes { get; set; }

        // ═════════════════════════════════════════════════════════════
        // Navigation Properties
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Employee record
        /// </summary>
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;

        /// <summary>
        /// Branch record
        /// </summary>
        [ForeignKey("BranchId")]
        public virtual Branch Branch { get; set; } = null!;
    }
}
