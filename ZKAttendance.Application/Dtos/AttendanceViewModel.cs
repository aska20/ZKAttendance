namespace ZKAttendance.Application.Dtos
{
    public class AttendanceViewModel
    {
        /// <summary>NEW. The grouping key is now the employee, not the biometric ID.</summary>
        public int EmployeeId { get; set; }

        /// <summary>The employee's primary biometric ID, for display only.</summary>
        public string BiometricUserId { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>Gregorian date, kept for sorting and filtering.</summary>
        public DateTime Date { get; set; }

        /// <summary>NEW. Bikram Sambat date for display, e.g. "2083-01-15".</summary>
        public string NepaliDate { get; set; } = string.Empty;

        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }

        public string BranchName { get; set; } = string.Empty;

        /// <summary>Kept so existing views do not break. Same as CheckInDeviceName.</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>NEW. Device that recorded the first punch of the day.</summary>
        public string CheckInDeviceName { get; set; } = string.Empty;

        /// <summary>NEW. Device that recorded the last punch of the day.</summary>
        public string CheckOutDeviceName { get; set; } = string.Empty;

        /// <summary>
        /// NEW. True when the employee checked in on one device and out on
        /// another - for example arriving at head office and leaving from the
        /// branch. Used to show a marker in the attendance table.
        /// </summary>
        public bool IsCrossDevice { get; set; }

        public double WorkingHours { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ShiftName { get; set; }
    }
}
