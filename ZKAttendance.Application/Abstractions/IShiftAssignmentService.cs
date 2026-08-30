using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    /// <summary>
    /// Which shift an employee is working on a given date.
    ///
    /// WHY THIS INTERFACE EXISTS
    /// -------------------------
    /// AttendanceCalculationService lives in Application and needs the shift to
    /// work out lateness. The implementation queries EmployeeShiftAssignments
    /// through EF Core, so it belongs in Infrastructure.
    ///
    /// Without this port, Application would have to reference the Infrastructure
    /// assembly to see the concrete class - an inner layer depending on an outer
    /// one, which is the exact thing the dependency rule forbids. It would not
    /// even compile, because Application has no project reference to
    /// Infrastructure and never will.
    /// </summary>
    public interface IShiftAssignmentService
    {
        /// <summary>
        /// The shift assignment effective on <paramref name="date"/>, or null
        /// when the employee has none and the caller should fall back to
        /// Employee.DefaultShift.
        /// </summary>
        Task<WorkShift?> GetEmployeeShiftForDate(int employeeId, DateTime date);
    }
}
