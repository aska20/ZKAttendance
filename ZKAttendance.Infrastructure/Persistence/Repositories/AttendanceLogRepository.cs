using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Persistence.Repositories
{
    public class AttendanceLogRepository
    {
        private readonly AttendanceDbContext _context;

        public AttendanceLogRepository(AttendanceDbContext context)
        {
            _context = context;
        }

        // ═══════════════════════════════════════════════════════
        // Basic CRUD
        // ═══════════════════════════════════════════════════════

        public async Task<AttendanceLog?> GetByIdAsync(long logId)
        {
            return await _context.AttendanceLogs
                .Include(a => a.Device)
                .Include(a => a.Branch)
                .FirstOrDefaultAsync(a => a.LogId == logId);
        }

        public async Task<AttendanceLog> AddAsync(AttendanceLog log)
        {
            _context.AttendanceLogs.Add(log);
            await _context.SaveChangesAsync();
            return log;
        }

        public async Task AddRangeAsync(List<AttendanceLog> logs)
        {
            _context.AttendanceLogs.AddRange(logs);
            await _context.SaveChangesAsync();
        }

        public async Task<AttendanceLog> UpdateAsync(AttendanceLog log)
        {
            _context.AttendanceLogs.Update(log);
            await _context.SaveChangesAsync();
            return log;
        }

        public async Task DeleteAsync(long logId)
        {
            var log = await _context.AttendanceLogs.FindAsync(logId);
            if (log != null)
            {
                _context.AttendanceLogs.Remove(log);
                await _context.SaveChangesAsync();
            }
        }

        // ═══════════════════════════════════════════════════════
        // Query Methods
        // ═══════════════════════════════════════════════════════

        public async Task<List<AttendanceLog>> GetByDateRangeAsync(
            DateTime fromDate,
            DateTime toDate,
            string? searchString = null,
            int? branchId = null,
            int? deviceId = null)
        {
            var query = _context.AttendanceLogs
                .Include(a => a.Device)
                .Include(a => a.Branch)
                .Where(a => a.AttendanceTime >= fromDate && a.AttendanceTime < toDate)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(a =>
                    a.BiometricUserId.Contains(searchString) ||
                    (a.Employee != null && a.Employee.EmployeeName.Contains(searchString))
                );
            }

            if (branchId.HasValue)
                query = query.Where(a => a.BranchId == branchId.Value);

            if (deviceId.HasValue)
                query = query.Where(a => a.DeviceId == deviceId.Value);

            return await query
                .OrderByDescending(a => a.AttendanceTime)
                .ToListAsync();
        }

        public async Task<List<AttendanceLog>> GetByEmployeeAndDateRangeAsync(
            string biometricUserId,
            DateTime fromDate,
            DateTime toDate)
        {
            return await _context.AttendanceLogs
                .Include(a => a.Device)
                .Include(a => a.Branch)
                .Where(a => a.BiometricUserId == biometricUserId
                         && a.AttendanceTime >= fromDate
                         && a.AttendanceTime < toDate)
                .OrderBy(a => a.AttendanceTime)
                .ToListAsync();
        }

        public async Task<List<AttendanceLog>> GetByEmployeeAndDateAsync(
            string biometricUserId,
            DateTime date)
        {
            var startDate = date.Date;
            var endDate = startDate.AddDays(1);

            return await _context.AttendanceLogs
                .Where(a => a.BiometricUserId == biometricUserId
                         && a.AttendanceTime >= startDate
                         && a.AttendanceTime < endDate)
                .OrderBy(a => a.AttendanceTime)
                .ToListAsync();
        }

        public async Task<List<AttendanceLog>> GetByDeviceAndDateRangeAsync(
            int deviceId,
            DateTime fromDate,
            DateTime toDate)
        {
            return await _context.AttendanceLogs
                .Where(a => a.DeviceId == deviceId
                         && a.AttendanceTime >= fromDate
                         && a.AttendanceTime < toDate)
                .OrderByDescending(a => a.AttendanceTime)
                .ToListAsync();
        }

        public async Task<List<AttendanceLog>> GetByBranchAndDateRangeAsync(
            int branchId,
            DateTime fromDate,
            DateTime toDate)
        {
            return await _context.AttendanceLogs
                .Where(a => a.BranchId == branchId
                         && a.AttendanceTime >= fromDate
                         && a.AttendanceTime < toDate)
                .OrderByDescending(a => a.AttendanceTime)
                .ToListAsync();
        }

        // ═══════════════════════════════════════════════════════
        // Duplicate Check
        // ═══════════════════════════════════════════════════════

        public async Task<bool> ExistsAsync(
            string biometricUserId,
            DateTime attendanceTime,
            int deviceId)
        {
            return await _context.AttendanceLogs.AnyAsync(a =>
                a.BiometricUserId == biometricUserId &&
                a.AttendanceTime == attendanceTime &&
                a.DeviceId == deviceId);
        }

        public async Task<List<AttendanceLog>> GetDuplicatesAsync(
            List<AttendanceLog> logs)
        {
            var duplicates = new List<AttendanceLog>();

            foreach (var log in logs)
            {
                var exists = await ExistsAsync(
                    log.BiometricUserId,
                    log.AttendanceTime,
                    log.DeviceId
                );

                if (exists)
                    duplicates.Add(log);
            }

            return duplicates;
        }

        // ═══════════════════════════════════════════════════════
        // Statistics
        // ═══════════════════════════════════════════════════════

        public async Task<int> GetTotalCountAsync()
        {
            return await _context.AttendanceLogs.CountAsync();
        }

        public async Task<int> GetCountByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            return await _context.AttendanceLogs
                .CountAsync(a => a.AttendanceTime >= fromDate && a.AttendanceTime < toDate);
        }

        public async Task<int> GetCountByEmployeeAndDateRangeAsync(
            string biometricUserId,
            DateTime fromDate,
            DateTime toDate)
        {
            return await _context.AttendanceLogs
                .CountAsync(a => a.BiometricUserId == biometricUserId
                              && a.AttendanceTime >= fromDate
                              && a.AttendanceTime < toDate);
        }

        public async Task<Dictionary<string, int>> GetDailyCountsAsync(
            DateTime fromDate,
            DateTime toDate)
        {
            var logs = await _context.AttendanceLogs
                .Where(a => a.AttendanceTime >= fromDate && a.AttendanceTime < toDate)
                .GroupBy(a => a.AttendanceTime.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            return logs.ToDictionary(
                x => x.Date.ToString("yyyy-MM-dd"),
                x => x.Count
            );
        }

        // ═══════════════════════════════════════════════════════
        // Sync Operations
        // ═══════════════════════════════════════════════════════

        public async Task<List<AttendanceLog>> GetUnsyncedLogsAsync()
        {
            return await _context.AttendanceLogs
                .Where(a => !a.IsSynced)
                .OrderBy(a => a.AttendanceTime)
                .ToListAsync();
        }

        public async Task MarkAsSyncedAsync(List<long> logIds)
        {
            var logs = await _context.AttendanceLogs
                .Where(a => logIds.Contains(a.LogId))
                .ToListAsync();

            foreach (var log in logs)
            {
                log.IsSynced = true;
                log.SyncedDate = DateTime.Now;
            }

            await _context.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════
        // Processing Operations
        // ═══════════════════════════════════════════════════════

        public async Task<List<AttendanceLog>> GetUnprocessedLogsAsync()
        {
            return await _context.AttendanceLogs
                .Where(a => !a.IsProcessed)
                .OrderBy(a => a.AttendanceTime)
                .ToListAsync();
        }

        public async Task MarkAsProcessedAsync(List<long> logIds)
        {
            var logs = await _context.AttendanceLogs
                .Where(a => logIds.Contains(a.LogId))
                .ToListAsync();

            foreach (var log in logs)
            {
                log.IsProcessed = true;
                log.ProcessedDate = DateTime.Now;
            }

            await _context.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════
        // Grouping for Daily Reports
        // ═══════════════════════════════════════════════════════

        public async Task<Dictionary<string, List<AttendanceLog>>> GroupByEmployeeAndDateAsync(
            DateTime fromDate,
            DateTime toDate)
        {
            var logs = await GetByDateRangeAsync(fromDate, toDate);

            return logs
                .GroupBy(a => $"{a.BiometricUserId}_{a.AttendanceTime.Date:yyyy-MM-dd}")
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(a => a.AttendanceTime).ToList()
                );
        }

        // ═══════════════════════════════════════════════════════
        // Latest Log
        // ═══════════════════════════════════════════════════════

        public async Task<AttendanceLog?> GetLatestLogAsync()
        {
            return await _context.AttendanceLogs
                .OrderByDescending(a => a.AttendanceTime)
                .FirstOrDefaultAsync();
        }

        public async Task<AttendanceLog?> GetLatestLogByEmployeeAsync(string biometricUserId)
        {
            return await _context.AttendanceLogs
                .Where(a => a.BiometricUserId == biometricUserId)
                .OrderByDescending(a => a.AttendanceTime)
                .FirstOrDefaultAsync();
        }
    }
}
