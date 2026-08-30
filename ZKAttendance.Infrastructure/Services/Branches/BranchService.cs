using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence.Repositories;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Branches
{
    public class BrancheService : IBrancheService
    {
        private readonly BranchRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<BrancheService> _logger;

        public BrancheService(
            BranchRepository repository,
            IMemoryCache cache,
            ILogger<BrancheService> logger)
        {
            _repository = repository;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<Branch>> GetAllBranchesAsync()
        {
            try
            {
                return await _repository.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the branch list");
                throw;
            }
        }

        public async Task<Branch?> GetBranchByIdAsync(int branchId)
        {
            try
            {
                return await _repository.GetByIdAsync(branchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the branch {BranchId}", branchId);
                throw;
            }
        }

        /// <summary>Get a branch by its code.</summary>
        public async Task<Branch?> GetBranchByCodeAsync(string branchCode)
        {
            try
            {
                return await _repository.GetByCodeAsync(branchCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching branch by code {BranchCode}", branchCode);
                throw;
            }
        }

        /// <summary>Get all branches with their devices in one query.</summary>
        public async Task<List<Branch>> GetAllBranchesWithDevicesAsync()
        {
            try
            {
                // Cache the expensive queries
                return await _cache.GetOrCreateAsync("branches_with_devices", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    return await _repository.GetAllWithDevicesAsync();
                }) ?? new List<Branch>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching branches with devices");
                throw;
            }
        }

        public async Task<Branch> CreateBranchAsync(Branch branch)
        {
            try
            {
                if (await _repository.CodeExistsAsync(branch.BranchCode))
                {
                    throw new InvalidOperationException($"Branch Code '{branch.BranchCode}' already exists");
                }

                branch.IsActive = true;
                branch.CreatedDate = DateTime.Now;

                var result = await _repository.AddAsync(branch);
                ClearBranchCache();

                _logger.LogInformation("Created branch: {BranchName}", branch.BranchName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating branch: {BranchName}", branch.BranchName);
                throw;
            }
        }

        public async Task<Branch> UpdateBranchAsync(Branch branch)
        {
            try
            {
                if (!await _repository.ExistsAsync(branch.BranchId))
                {
                    throw new InvalidOperationException($"Branch not found");
                }

                if (await _repository.CodeExistsAsync(branch.BranchCode, branch.BranchId))
                {
                    throw new InvalidOperationException($"Branch Code '{branch.BranchCode}' already exists");
                }

                branch.ModifiedDate = DateTime.Now;
                var result = await _repository.UpdateAsync(branch);
                ClearBranchCache();

                _logger.LogInformation("Updated branch: {BranchName}", branch.BranchName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating branch {BranchId}", branch.BranchId);
                throw;
            }
        }

        public async Task DeleteBranchAsync(int branchId)
        {
            try
            {
                await _repository.SoftDeleteAsync(branchId);
                ClearBranchCache();
                _logger.LogInformation("Branch deleted {BranchId}", branchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting branch {BranchId}", branchId);
                throw;
            }
        }

        private void ClearBranchCache()
        {
            _cache.Remove("active_branches");
            _cache.Remove("branches");
            _cache.Remove("branches_with_devices"); 
        }
    }
}
