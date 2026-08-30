using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    public interface IDepartmentService
    {
        Task<List<Department>> GetAllDepartmentsAsync();
        Task<Department?> GetDepartmentByIdAsync(int departmentId);
        Task<List<Department>> GetTopLevelDepartmentsAsync();
        Task<List<Department>> GetSubDepartmentsAsync(int parentDepartmentId);
        Task<Department> CreateDepartmentAsync(Department department);
        Task<Department> UpdateDepartmentAsync(Department department);
        Task DeleteDepartmentAsync(int departmentId);
        Task<bool> CanDeleteDepartmentAsync(int departmentId);
    }
}
