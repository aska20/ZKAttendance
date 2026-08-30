using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// Links one employee to one device, AND records the user ID that this
    /// particular device knows them by.
    ///
    /// WHAT CHANGED AND WHY
    /// --------------------
    /// The original class only stored EmployeeId + DeviceId. The biometric ID
    /// lived on Employee.BiometricUserId with a UNIQUE index, which forced every
    /// device to use the same number for the same person.
    ///
    /// That breaks in practice. When you enrol Ramesh on the Head Office machine
    /// it may assign 1017; when you enrol him again on the Branch machine it may
    /// assign 88, because 1017 is already taken there by someone else. Storing
    /// the ID here - once per employee per device - is what makes several
    /// machines work together.
    ///
    /// Employee.BiometricUserId is kept as the "primary" ID for backwards
    /// compatibility and for reports, but the sync path resolves through
    /// this table.
    /// </summary>
    [Table("EmployeeDevices")]
    public class EmployeeDevice
    {
        [Key]
        public int EmployeeDeviceId { get; set; }

        [Required]
        public int EmployeeId { get; set; }

        [Required]
        public int DeviceId { get; set; }

        /// <summary>
        /// NEW. The enrol number this device assigned to this employee.
        /// This is the value that arrives in every punch record from the device.
        /// </summary>
        [Required]
        [StringLength(12)]
        [Column(TypeName = "varchar(12)")]
        public string BiometricUserId { get; set; } = string.Empty;

        /// <summary>
        /// NEW. True once the fingerprint template is confirmed on this device.
        /// Lets the portal show "enrolled on 2 of 3 devices".
        /// </summary>
        public bool IsEnrolled { get; set; } = false;

        /// <summary>NEW. When enrolment on this device was confirmed.</summary>
        public DateTime? EnrolledDate { get; set; }

        public bool IsActive { get; set; } = true;

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public virtual Employee? Employee { get; set; }

        [ForeignKey(nameof(DeviceId))]
        public virtual Device? Device { get; set; }
    }
}
