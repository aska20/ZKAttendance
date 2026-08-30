using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Attendances
{
    public class AttendanceQueryService
    {
        private readonly AttendanceDbContext _context;

        public AttendanceQueryService(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<List<AttendanceLog>> GetFilteredLogs(
            string? searchString,
            DateTime? fromDate,
            DateTime? toDate,
            int? branchId,
            int? deviceId)
        {
            var query = _context.AttendanceLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
                query = query.Where(a => a.BiometricUserId.Contains(searchString));

            if (fromDate.HasValue)
                query = query.Where(a => a.AttendanceTime.Date >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(a => a.AttendanceTime.Date <= toDate.Value.Date);

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(a => a.BranchId == branchId.Value);

            if (deviceId.HasValue && deviceId.Value > 0)
                query = query.Where(a => a.DeviceId == deviceId.Value);

            return await query.ToListAsync();
        }

        /// <summary>
        /// CHANGED: keyed by EmployeeId, previously by BiometricUserId.
        ///
        /// The same person can hold a different biometric ID on each device, so
        /// a dictionary keyed by biometric ID would either miss rows or throw
        /// on a duplicate key. EmployeeId is the only identifier that is stable
        /// across devices.
        /// </summary>
        public async Task<Dictionary<int, Employee>> GetEmployeesDictionary(List<int> employeeIds)
        {
            return await _context.Employees
                .AsNoTracking()
                .Include(e => e.DefaultShift)
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId);
        }

        /// <summary>
        /// NEW. Punches whose biometric ID is not mapped to any employee on
        /// that device. They are stored rather than discarded, and surfaced
        /// here so HR can finish the mapping.
        /// </summary>
        public async Task<List<AttendanceLog>> GetUnmappedLogs(DateTime? from, DateTime? to)
        {
            var query = _context.AttendanceLogs.AsNoTracking()
                .Where(a => a.EmployeeId == null);

            if (from.HasValue) query = query.Where(a => a.AttendanceTime.Date >= from.Value.Date);
            if (to.HasValue) query = query.Where(a => a.AttendanceTime.Date <= to.Value.Date);

            return await query.OrderByDescending(a => a.AttendanceTime).ToListAsync();
        }

        public async Task<Dictionary<int, string>> GetBranchesDictionary(List<int> branchIds)
        {
            return await _context.Branches
                .AsNoTracking()
                .Where(b => branchIds.Contains(b.BranchId))
                .ToDictionaryAsync(b => b.BranchId, b => b.BranchName);
        }

        public async Task<Dictionary<int, string>> GetDevicesDictionary(List<int> deviceIds)
        {
            return await _context.Devices
                .AsNoTracking()
                .Where(d => deviceIds.Contains(d.DeviceId))
                .ToDictionaryAsync(d => d.DeviceId, d => d.DeviceName);
        }
    }
}
