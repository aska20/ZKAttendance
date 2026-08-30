using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Departments
{
    public class DepartmentService : IDepartmentService
    {
        private readonly DepartmentRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DepartmentService> _logger;

        public DepartmentService(
            DepartmentRepository repository,
            IMemoryCache cache,
            ILogger<DepartmentService> logger)
        {
            _repository = repository;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<Department>> GetAllDepartmentsAsync()
        {
            try
            {
                return await _repository.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the department list");
                throw;
            }
        }

        public async Task<Department?> GetDepartmentByIdAsync(int departmentId)
        {
            try
            {
                return await _repository.GetByIdAsync(departmentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the department {DepartmentId}", departmentId);
                throw;
            }
        }

        public async Task<List<Department>> GetTopLevelDepartmentsAsync()
        {
            try
            {
                return await _repository.GetTopLevelDepartmentsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching root departments");
                throw;
            }
        }

        public async Task<List<Department>> GetSubDepartmentsAsync(int parentDepartmentId)
        {
            try
            {
                return await _repository.GetSubDepartmentsAsync(parentDepartmentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching sub-departments {DepartmentId}", parentDepartmentId);
                throw;
            }
        }

        public async Task<Department> CreateDepartmentAsync(Department department)
        {
            try
            {
                // Reject a duplicate code
                if (!string.IsNullOrEmpty(department.DepartmentCode))
                {
                    if (await _repository.CodeExistsAsync(department.DepartmentCode))
                    {
                        throw new InvalidOperationException(
                            $"Department Code '{department.DepartmentCode}' already exists");
                    }
                }

                department.IsActive = true;
                department.CreatedDate = DateTime.Now;

                var result = await _repository.AddAsync(department);

                ClearDepartmentCache();
                _logger.LogInformation("Created department: {DepartmentName}", department.DepartmentName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating department: {DepartmentName}", department.DepartmentName);
                throw;
            }
        }

        public async Task<Department> UpdateDepartmentAsync(Department department)
        {
            try
            {
                if (!await _repository.ExistsAsync(department.DepartmentId))
                {
                    throw new InvalidOperationException("Department not found");
                }

                // Reject a duplicate code
                if (!string.IsNullOrEmpty(department.DepartmentCode))
                {
                    if (await _repository.CodeExistsAsync(department.DepartmentCode, department.DepartmentId))
                    {
                        throw new InvalidOperationException(
                            $"Department Code '{department.DepartmentCode}' already exists");
                    }
                }

                // Prevent a department from becoming its own parent
                if (department.ParentDepartmentId == department.DepartmentId)
                {
                    throw new InvalidOperationException("A department cannot be its own parent");
                }

                department.ModifiedDate = DateTime.Now;
                var result = await _repository.UpdateAsync(department);

                ClearDepartmentCache();
                _logger.LogInformation("Updated department: {DepartmentName}", department.DepartmentName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating department {DepartmentId}", department.DepartmentId);
                throw;
            }
        }

        public async Task DeleteDepartmentAsync(int departmentId)
        {
            try
            {
                // Make sure no employee is in this department
                var employeeCount = await _repository.GetEmployeeCountAsync(departmentId);
                if (employeeCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete the department because it contains {employeeCount} employee");
                }

                // Make sure there are no sub-departments
                var subDepartments = await _repository.GetSubDepartmentsAsync(departmentId);
                if (subDepartments.Any())
                {
                    throw new InvalidOperationException(
                        $"Cannot delete the department because it contains {subDepartments.Count} sub-department");
                }

                await _repository.SoftDeleteAsync(departmentId);

                ClearDepartmentCache();
                _logger.LogInformation("Department deleted {DepartmentId}", departmentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting department {DepartmentId}", departmentId);
                throw;
            }
        }

        public async Task<bool> CanDeleteDepartmentAsync(int departmentId)
        {
            try
            {
                var employeeCount = await _repository.GetEmployeeCountAsync(departmentId);
                var subDepartments = await _repository.GetSubDepartmentsAsync(departmentId);

                return employeeCount == 0 && !subDepartments.Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking whether the department can be deleted {DepartmentId}", departmentId);
                throw;
            }
        }

        private void ClearDepartmentCache()
        {
            _cache.Remove("active_departments");
            _cache.Remove("departments");
        }
    }
}
