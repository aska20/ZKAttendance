using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Persistence.Repositories
{
    public class EmployeeRepository
    {
        private readonly AttendanceDbContext _context;

        public EmployeeRepository(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<List<Employee>> GetAllAsync(bool includeInactive = false)
        {
            var query = _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .AsQueryable();

            if (!includeInactive)
                query = query.Where(e => e.IsActive);

            return await query.OrderBy(e => e.EmployeeName).ToListAsync();
        }

        public async Task<Employee?> GetByIdAsync(int employeeId)
        {
            return await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        }

        public async Task<Employee?> GetByBiometricIdAsync(string biometricUserId)
        {
            return await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .FirstOrDefaultAsync(e => e.BiometricUserId == biometricUserId);
        }

        /// <summary>
        /// Get employees by department
        /// </summary>
        public async Task<List<Employee>> GetByDepartmentIdAsync(int departmentId)
        {
            return await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .Where(e => e.DepartmentId == departmentId && e.IsActive)
                .OrderBy(e => e.EmployeeName)
                .ToListAsync();
        }

        public async Task<bool> BiometricIdExistsAsync(string biometricUserId, int? excludeEmployeeId = null)
        {
            var query = _context.Employees.Where(e => e.BiometricUserId == biometricUserId);

            if (excludeEmployeeId.HasValue)
                query = query.Where(e => e.EmployeeId != excludeEmployeeId.Value);

            return await query.AnyAsync();
        }

        public async Task<bool> ExistsAsync(int employeeId)
        {
            return await _context.Employees.AnyAsync(e => e.EmployeeId == employeeId);
        }

        public async Task<Employee> AddAsync(Employee employee)
        {
            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();
            return employee;
        }

        public async Task<Employee> UpdateAsync(Employee employee)
        {
            _context.Employees.Update(employee);
            await _context.SaveChangesAsync();
            return employee;
        }

        public async Task SoftDeleteAsync(int employeeId)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee != null)
            {
                employee.IsActive = false;
                employee.ModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<Dictionary<string, Employee>> GetEmployeesDictionaryAsync(List<string> biometricUserIds)
        {
            return await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .Where(e => biometricUserIds.Contains(e.BiometricUserId))
                .ToDictionaryAsync(e => e.BiometricUserId, e => e);
        }

        public async Task<int> GetActiveEmployeeCountAsync()
        {
            return await _context.Employees.CountAsync(e => e.IsActive);
        }

        /// <summary>
        /// Get employees with their departments, for reports
        /// </summary>
        public async Task<List<Employee>> GetAllWithDepartmentsAsync()
        {
            return await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .Where(e => e.IsActive)
                .OrderBy(e => e.EmployeeName)
                .ToListAsync();
        }

        /// <summary>
        /// Get employees using several filters
        /// </summary>
        public async Task<List<Employee>> GetFilteredEmployeesAsync(int? departmentId = null)
        {
            var query = _context.Employees
                .Include(e => e.Department)
                .Include(e => e.DefaultShift)
                .Where(e => e.IsActive);

            if (departmentId.HasValue)
                query = query.Where(e => e.DepartmentId == departmentId.Value);

            return await query
                .OrderBy(e => e.EmployeeName)
                .ToListAsync();
        }
    }
}
