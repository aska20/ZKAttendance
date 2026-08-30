using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    public interface IBrancheService
    {
        Task<List<Branch>> GetAllBranchesAsync();
        Task<Branch?> GetBranchByIdAsync(int branchId);
        Task<Branch?> GetBranchByCodeAsync(string branchCode); 
        Task<List<Branch>> GetAllBranchesWithDevicesAsync();   
        Task<Branch> CreateBranchAsync(Branch branch);
        Task<Branch> UpdateBranchAsync(Branch branch);
        Task DeleteBranchAsync(int branchId);
    }
}
