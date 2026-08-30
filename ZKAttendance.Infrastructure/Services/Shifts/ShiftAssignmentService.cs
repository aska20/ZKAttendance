using ZKAttendance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Shifts
{
    public class ShiftAssignmentService : IShiftAssignmentService
    {
        private readonly AttendanceDbContext _context;

        public ShiftAssignmentService(AttendanceDbContext context)
        {
            _context = context;
        }

        // Get the employee's shift for a given date
        public async Task<WorkShift?> GetEmployeeShiftForDate(int employeeId, DateTime date)
        {
            var assignment = await _context.EmployeeShiftAssignments
                .Include(e => e.Shift)
                .Where(e => e.EmployeeId == employeeId
                            && e.IsActive
                            && e.EffectiveFrom <= date
                            && (e.EffectiveTo == null || e.EffectiveTo >= date))
                .OrderByDescending(e => e.EffectiveFrom)
                .FirstOrDefaultAsync();

            return assignment?.Shift;
        }

    }
}
