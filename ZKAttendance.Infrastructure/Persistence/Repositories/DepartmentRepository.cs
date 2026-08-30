using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Persistence.Repositories
{
    public class DepartmentRepository
    {
        private readonly AttendanceDbContext _context;

        public DepartmentRepository(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<List<Department>> GetAllAsync(bool includeInactive = false)
        {
            var query = _context.Departments
                .Include(d => d.ParentDepartment)
                .AsQueryable();

            if (!includeInactive)
                query = query.Where(d => d.IsActive);

            return await query.OrderBy(d => d.DepartmentName).ToListAsync();
        }

        public async Task<Department?> GetByIdAsync(int departmentId)
        {
            return await _context.Departments
                .Include(d => d.ParentDepartment)
                .Include(d => d.SubDepartments)
                .Include(d => d.Employees)
                .FirstOrDefaultAsync(d => d.DepartmentId == departmentId);
        }

        public async Task<Department?> GetByCodeAsync(string departmentCode)
        {
            return await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentCode == departmentCode);
        }

        public async Task<List<Department>> GetTopLevelDepartmentsAsync()
        {
            return await _context.Departments
                .Where(d => d.ParentDepartmentId == null && d.IsActive)
                .OrderBy(d => d.DepartmentName)
                .ToListAsync();
        }

        public async Task<List<Department>> GetSubDepartmentsAsync(int parentDepartmentId)
        {
            return await _context.Departments
                .Where(d => d.ParentDepartmentId == parentDepartmentId && d.IsActive)
                .OrderBy(d => d.DepartmentName)
                .ToListAsync();
        }

        public async Task<bool> CodeExistsAsync(string departmentCode, int? excludeDepartmentId = null)
        {
            var query = _context.Departments.Where(d => d.DepartmentCode == departmentCode);

            if (excludeDepartmentId.HasValue)
                query = query.Where(d => d.DepartmentId != excludeDepartmentId.Value);

            return await query.AnyAsync();
        }

        public async Task<bool> ExistsAsync(int departmentId)
        {
            return await _context.Departments.AnyAsync(d => d.DepartmentId == departmentId);
        }

        public async Task<Department> AddAsync(Department department)
        {
            _context.Departments.Add(department);
            await _context.SaveChangesAsync();
            return department;
        }

        public async Task<Department> UpdateAsync(Department department)
        {
            _context.Departments.Update(department);
            await _context.SaveChangesAsync();
            return department;
        }

        public async Task SoftDeleteAsync(int departmentId)
        {
            var department = await _context.Departments.FindAsync(departmentId);
            if (department != null)
            {
                department.IsActive = false;
                department.ModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<int> GetEmployeeCountAsync(int departmentId)
        {
            return await _context.Employees
                .CountAsync(e => e.DepartmentId == departmentId && e.IsActive);
        }
    }
}
