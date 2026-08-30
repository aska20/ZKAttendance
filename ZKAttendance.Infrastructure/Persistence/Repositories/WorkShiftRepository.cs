using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Persistence.Repositories
{
    public class WorkShiftRepository
    {
        private readonly AttendanceDbContext _context;

        public WorkShiftRepository(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<List<WorkShift>> GetAllAsync(bool includeInactive = false)
        {
            var query = _context.WorkShifts.AsQueryable();

            if (!includeInactive)
                query = query.Where(s => s.IsActive);

            return await query.OrderBy(s => s.StartTime).ToListAsync();
        }

        public async Task<WorkShift?> GetByIdAsync(int shiftId)
        {
            return await _context.WorkShifts
                .Include(s => s.Employees)
                .FirstOrDefaultAsync(s => s.ShiftId == shiftId);
        }

        public async Task<WorkShift?> GetByNameAsync(string shiftName)
        {
            return await _context.WorkShifts
                .FirstOrDefaultAsync(s => s.ShiftName == shiftName);
        }

        public async Task<bool> NameExistsAsync(string shiftName, int? excludeShiftId = null)
        {
            var query = _context.WorkShifts.Where(s => s.ShiftName == shiftName);

            if (excludeShiftId.HasValue)
                query = query.Where(s => s.ShiftId != excludeShiftId.Value);

            return await query.AnyAsync();
        }

        public async Task<bool> ExistsAsync(int shiftId)
        {
            return await _context.WorkShifts.AnyAsync(s => s.ShiftId == shiftId);
        }

        public async Task<WorkShift> AddAsync(WorkShift shift)
        {
            _context.WorkShifts.Add(shift);
            await _context.SaveChangesAsync();
            return shift;
        }

        public async Task<WorkShift> UpdateAsync(WorkShift shift)
        {
            _context.WorkShifts.Update(shift);
            await _context.SaveChangesAsync();
            return shift;
        }

        public async Task SoftDeleteAsync(int shiftId)
        {
            var shift = await _context.WorkShifts.FindAsync(shiftId);
            if (shift != null)
            {
                shift.IsActive = false;
                shift.ModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<int> GetEmployeeCountAsync(int shiftId)
        {
            return await _context.Employees
                .CountAsync(e => e.DefaultShiftId == shiftId && e.IsActive);
        }

        public async Task<List<WorkShift>> GetActiveShiftsForTimeAsync(TimeSpan currentTime)
        {
            return await _context.WorkShifts
                .Where(s => s.IsActive)
                .Where(s => s.StartTime <= currentTime && s.EndTime >= currentTime)
                .ToListAsync();
        }
    }
}
