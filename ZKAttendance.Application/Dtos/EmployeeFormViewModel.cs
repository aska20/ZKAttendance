using System.ComponentModel.DataAnnotations;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Dtos
{
    public class EmployeeFormViewModel
    {
        // ══════════════════════════════════════════════════════════════
        // Basic employee information
        // ══════════════════════════════════════════════════════════════
        public int EmployeeId { get; set; }

            [Required(ErrorMessage = "Biometric ID is required")]
            [Display(Name = "Biometric ID")]
        public string BiometricUserId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee name is required")]
        [Display(Name = "Employee Name")]
        [StringLength(100, ErrorMessage = "Employee name must not exceed 100 characters")]
        public string EmployeeName { get; set; } = string.Empty;

        [Display(Name = "National ID")]
        [StringLength(20)]
        public string? SSN { get; set; }

        [Display(Name = "Gender")]
        [StringLength(10)]
        public string? Gender { get; set; }

        [Display(Name = "Job Title")]
        [StringLength(50)]
        public string? Title { get; set; }

        [Display(Name = "Phone Number")]
        [Phone(ErrorMessage = "Invalid phone number")]
        [StringLength(15)]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Hire Date")]
        [DataType(DataType.Date)]
        public DateTime? HireDate { get; set; }

        // ══════════════════════════════════════════════════════════════
        // Relationships
        // ══════════════════════════════════════════════════════════════
        [Display(Name = "Department")]
        public int? DepartmentId { get; set; }

        [Display(Name = "Default Shift")]
        public int? DefaultShiftId { get; set; }

        // ══════════════════════════════════════════════════════════════
        // Attendance settings
        // ══════════════════════════════════════════════════════════════
        [Display(Name = "Track Attendance")]
        public bool CheckAttendance { get; set; } = true;

        [Display(Name = "Track Late Arrival")]
        public bool CheckLate { get; set; } = true;

        [Display(Name = "Track Early Departure")]
        public bool CheckEarly { get; set; } = true;

        [Display(Name = "Calculate Overtime")]
        public bool CheckOvertime { get; set; } = true;

        [Display(Name = "Track Leave")]
        public bool CheckHoliday { get; set; } = true;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        // ══════════════════════════════════════════════════════════════
        // NEW: branches and devices
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Branches grouped by city, with their devices
        /// </summary>
        public Dictionary<string, List<BranchDeviceViewModel>> BranchesByCity { get; set; } = new();

        /// <summary>
        /// Selected device IDs (for save/update)
        /// </summary>
        public List<int> SelectedDeviceIds { get; set; } = new();

        /// <summary>
        /// Selected branch IDs (optional, display only)
        /// </summary>
        public List<int> SelectedBranchIds { get; set; } = new();

        // ══════════════════════════════════════════════════════════════
        // Kept for backwards compatibility
        // ══════════════════════════════════════════════════════════════
        public List<BranchCheckboxItem>? AvailableBranches { get; set; }
        public bool SelectAllBranches { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // Helper class kept for backwards compatibility
    // ══════════════════════════════════════════════════════════════
    public class BranchCheckboxItem
    {
        public int BranchId { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}