using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Common
{
    public class LookupService
    {
        private readonly AttendanceDbContext _context;
        private readonly IMemoryCache _cache;

        public LookupService(AttendanceDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ═══════════════════════════════════════════════════════
        // Branches
        // ═══════════════════════════════════════════════════════
        public async Task<List<Branch>> GetActiveBranchesAsync()
        {
            return await _cache.GetOrCreateAsync("active_branches", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Branches
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.BranchCode)
                    .ToListAsync();
            }) ?? new List<Branch>();
        }

        // ═══════════════════════════════════════════════════════
        // Devices
        // ═══════════════════════════════════════════════════════
        public async Task<List<Device>> GetActiveDevicesAsync()
        {
            return await _cache.GetOrCreateAsync("active_devices", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Devices
                    .Include(d => d.Branch)
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.DeviceName)
                    .ToListAsync();
            }) ?? new List<Device>();
        }

        public async Task<List<Device>> GetActiveDevicesByBranchAsync(int branchId)
        {
            return await _cache.GetOrCreateAsync($"active_devices_branch_{branchId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Devices
                    .Where(d => d.BranchId == branchId && d.IsActive)
                    .OrderBy(d => d.DeviceName)
                    .ToListAsync();
            }) ?? new List<Device>();
        }

        // ═══════════════════════════════════════════════════════
        // Employees
        // ═══════════════════════════════════════════════════════
        public async Task<List<Employee>> GetActiveEmployeesAsync()
        {
            return await _cache.GetOrCreateAsync("active_employees", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await _context.Employees
                    .Include(e => e.Department)
                    .Include(e => e.DefaultShift)
                    .Where(e => e.IsActive)
                    .OrderBy(e => e.EmployeeName)
                    .ToListAsync();
            }) ?? new List<Employee>();
        }

        public async Task<List<Employee>> GetActiveEmployeesByDepartmentAsync(int departmentId)
        {
            return await _cache.GetOrCreateAsync($"active_employees_dept_{departmentId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await _context.Employees
                    .Include(e => e.Department)
                    .Include(e => e.DefaultShift)
                    .Where(e => e.DepartmentId == departmentId && e.IsActive)
                    .OrderBy(e => e.EmployeeName)
                    .ToListAsync();
            }) ?? new List<Employee>();
        }

        // ═══════════════════════════════════════════════════════
        // Departments
        // ═══════════════════════════════════════════════════════
        public async Task<List<Department>> GetActiveDepartmentsAsync()
        {
            return await _cache.GetOrCreateAsync("active_departments", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Departments
                    .Include(d => d.ParentDepartment)
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.DepartmentName)
                    .ToListAsync();
            }) ?? new List<Department>();
        }

        public async Task<List<Department>> GetTopLevelDepartmentsAsync()
        {
            return await _cache.GetOrCreateAsync("top_level_departments", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Departments
                    .Where(d => d.ParentDepartmentId == null && d.IsActive)
                    .OrderBy(d => d.DepartmentName)
                    .ToListAsync();
            }) ?? new List<Department>();
        }

        // ═══════════════════════════════════════════════════════
        // Work Shifts
        // ═══════════════════════════════════════════════════════
        public async Task<List<WorkShift>> GetActiveShiftsAsync()
        {
            return await _cache.GetOrCreateAsync("active_shifts", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.WorkShifts
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.StartTime)
                    .ToListAsync();
            }) ?? new List<WorkShift>();
        }

        // ═══════════════════════════════════════════════════════
        // Cache Management
        // ═══════════════════════════════════════════════════════
        public void ClearAllCache()
        {
            _cache.Remove("active_branches");
            _cache.Remove("active_devices");
            _cache.Remove("active_employees");
            _cache.Remove("active_departments");
            _cache.Remove("top_level_departments");
            _cache.Remove("active_shifts");
        }

        public void ClearBranchCache()
        {
            _cache.Remove("active_branches");
        }

        public void ClearDeviceCache()
        {
            _cache.Remove("active_devices");
            // Clear the per-branch device cache
            var keys = _context.Branches.Select(b => $"active_devices_branch_{b.BranchId}").ToList();
            foreach (var key in keys)
            {
                _cache.Remove(key);
            }
        }

        public void ClearEmployeeCache()
        {
            _cache.Remove("active_employees");
            // Clear the per-department employee cache
            var keys = _context.Departments.Select(d => $"active_employees_dept_{d.DepartmentId}").ToList();
            foreach (var key in keys)
            {
                _cache.Remove(key);
            }
        }

        public void ClearDepartmentCache()
        {
            _cache.Remove("active_departments");
            _cache.Remove("top_level_departments");
        }

        public void ClearShiftCache()
        {
            _cache.Remove("active_shifts");
        }
    }
}
