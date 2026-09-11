using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    [Table("Departments")]
    public class Department
    {
        [Key]
        public int DepartmentId { get; set; }

        [Required]
        [StringLength(100)]
        public string DepartmentName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? DepartmentCode { get; set; }

        public int? ParentDepartmentId { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// The shift everyone in this department works, unless an individual
        /// has their own. Nullable: a department with no shift behaves exactly
        /// as it did before, falling back to the office hours in Settings.
        /// </summary>
        public int? DefaultShiftId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }

        // Navigation Properties
        [ForeignKey("ParentDepartmentId")]
        public virtual Department? ParentDepartment { get; set; }
        public virtual WorkShift? DefaultShift { get; set; }

        public virtual ICollection<Department> SubDepartments { get; set; } = new List<Department>();

        public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }
}
