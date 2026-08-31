using System.ComponentModel.DataAnnotations;

namespace ZKAttendance.Application.Dtos.Api
{
    // ── Branches ───────────────────────────────────────────────────────

    public class BranchRequest
    {
        [Required, StringLength(20)]
        public string BranchCode { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string BranchName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? City { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(20)]
        public string? ContactPhone { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ── Work shifts ────────────────────────────────────────────────────

    /// <summary>
    /// Times are "HH:mm" or "HH:mm:ss" strings — they bind to TimeSpan.
    /// Everything except the two required times has a sensible default that
    /// matches the WorkShift entity.
    /// </summary>
    public class WorkShiftRequest
    {
        [Required, StringLength(100)]
        public string ShiftName { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public TimeSpan StartTime { get; set; }

        [Required]
        public TimeSpan EndTime { get; set; }

        public int LateMinutes { get; set; } = 15;
        public int EarlyMinutes { get; set; } = 15;
        public int BreakMinutes { get; set; } = 0;
        public bool IsBreakPaid { get; set; } = true;
        public int WorkMinutes { get; set; } = 480;
        public bool RequireCheckIn { get; set; } = true;
        public bool RequireCheckOut { get; set; } = true;
        public bool IsOvernight { get; set; } = false;
        public int OvertimeStartMinutes { get; set; } = 30;
        public double MinHoursForFullDay { get; set; } = 4.0;
        public double MaxRegularHours { get; set; } = 10.0;
        public int RoundingMinutes { get; set; } = 0;

        /// <summary>Comma-separated weekday numbers, e.g. "0,1,2,3,4,6" (Sun-Thu + Sat).</summary>
        public string? WorkDays { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ── Account ────────────────────────────────────────────────────────

    public class ChangePasswordRequest
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8)]
        public string NewPassword { get; set; } = string.Empty;
    }

    // ── Departments ────────────────────────────────────────────────────

    public class DepartmentRequest
    {
        [Required, StringLength(100)]
        public string DepartmentName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? DepartmentCode { get; set; }

        /// <summary>Parent department for a sub-department. Null for a top-level one.</summary>
        public int? ParentDepartmentId { get; set; }

        [StringLength(250)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ── Employees ──────────────────────────────────────────────────────

    public class EmployeeRequest
    {
        [Required, StringLength(100)]
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>
        /// The enrol number from the biometric device. Leave empty and the
        /// system allocates the next free one.
        /// </summary>
        [StringLength(12)]
        public string? BiometricUserId { get; set; }

        public int? DepartmentId { get; set; }
        public int? DefaultShiftId { get; set; }

        [StringLength(20)]
        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? Title { get; set; }

        [StringLength(150), EmailAddress]
        public string? Email { get; set; }

        [StringLength(20)]
        public string? SSN { get; set; }

        [StringLength(10)]
        public string? Gender { get; set; }

        public DateTime? BirthDate { get; set; }

        /// <summary>Gregorian date. The BS equivalent is derived, never stored as input.</summary>
        public DateTime? HireDate { get; set; }

        // Attendance tracking flags — the entity carries these; the old form set them.
        public bool CheckAttendance { get; set; } = true;
        public bool CheckLate { get; set; } = true;
        public bool CheckEarly { get; set; } = true;
        public bool CheckOvertime { get; set; } = true;
        public bool CheckHoliday { get; set; } = true;

        public bool IsActive { get; set; } = true;
    }

    /// <summary>Body for POST /api/Employees/{id}/enroll-on-devices.</summary>
    public class EnrollOnDevicesRequest
    {
        /// <summary>Devices this employee should exist on.</summary>
        [Required]
        public List<int> DeviceIds { get; set; } = new();

        /// <summary>
        /// Optional per-device enrol number, keyed by DeviceId.
        /// Omit a device and it inherits the employee's primary biometric ID.
        ///
        /// This exists because each machine allocates its own numbers: someone
        /// can be 1017 at head office and 88 at the branch, because 1017 was
        /// already taken there.
        /// </summary>
        public Dictionary<int, string>? DeviceUserIds { get; set; }
    }

    // ── Devices ────────────────────────────────────────────────────────

    public class DeviceRequest
    {
        [Required, StringLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        [Required, StringLength(45)]
        public string DeviceIP { get; set; } = string.Empty;

        public int DevicePort { get; set; } = 4370;

        [StringLength(50)]
        public string? SerialNumber { get; set; }

        [StringLength(50)]
        public string? DeviceModel { get; set; }

        [Required]
        public int BranchId { get; set; }

        /// <summary>
        /// "Master" or "Slave". Only one active master is allowed — the
        /// database enforces it with a filtered unique index, so a second one
        /// is rejected rather than silently accepted.
        /// </summary>
        public string Role { get; set; } = "Slave";

        public bool IsActive { get; set; } = true;
    }

    // ── Attendance ─────────────────────────────────────────────────────

    /// <summary>Body for POST /api/Attendance/manual.</summary>
    public class ManualAttendanceRequest
    {
        [Required]
        public int EmployeeId { get; set; }

        [Required]
        public int DeviceId { get; set; }

        /// <summary>Gregorian date and time of the punch.</summary>
        [Required]
        public DateTime PunchTime { get; set; }

        /// <summary>"CheckIn", "CheckOut", "BreakOut", "BreakIn".</summary>
        public string AttendanceType { get; set; } = "CheckIn";

        /// <summary>Why this was entered by hand. Recorded for audit.</summary>
        [StringLength(250)]
        public string? Notes { get; set; }
    }

    // ── Auth ───────────────────────────────────────────────────────────

    public class RegisterRequest
    {
        [Required, StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8)]
        public string Password { get; set; } = string.Empty;

        /// <summary>"Admin", "HR" or "Employee". Defaults to Employee.</summary>
        public string Role { get; set; } = "Employee";

        /// <summary>
        /// Optional. The employee's biometric/enrol number. When supplied (or
        /// when the email matches an existing employee) the new account is
        /// linked to that Employee record instead of standing alone.
        /// </summary>
        [StringLength(12)]
        public string? BiometricUserId { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshRequest
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    // ── Shared ─────────────────────────────────────────────────────────

    /// <summary>Uniform error body so clients can parse failures consistently.</summary>
    public class ApiError
    {
        public string Message { get; set; } = string.Empty;
        public string? Detail { get; set; }

        public static ApiError From(string message, string? detail = null)
            => new() { Message = message, Detail = detail };
    }
}
