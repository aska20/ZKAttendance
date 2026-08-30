using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Persistence.Repositories
{
    public class BranchRepository
    {
        private readonly AttendanceDbContext _context;

        public BranchRepository(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<List<Branch>> GetAllAsync(bool includeInactive = false)
        {
            var query = _context.Branches
                .Include(b => b.Devices)
                .AsQueryable();

            if (!includeInactive)
                query = query.Where(b => b.IsActive);

            return await query.OrderBy(b => b.BranchCode).ToListAsync();
        }

        /// <summary>Get all branches with their active devices.</summary>
        public async Task<List<Branch>> GetAllWithDevicesAsync()
        {
            return await _context.Branches
                .Include(b => b.Devices.Where(d => d.IsActive))
                .Where(b => b.IsActive)
                .OrderBy(b => b.BranchCode)
                .ToListAsync();
        }

        public async Task<Branch?> GetByIdAsync(int branchId)
        {
            return await _context.Branches
                .Include(b => b.Devices)
                .FirstOrDefaultAsync(b => b.BranchId == branchId);
        }

        public async Task<Branch?> GetByCodeAsync(string branchCode)
        {
            return await _context.Branches
                .Include(b => b.Devices.Where(d => d.IsActive))
                .FirstOrDefaultAsync(b => b.BranchCode == branchCode);
        }

        public async Task<bool> ExistsAsync(int branchId)
        {
            return await _context.Branches.AnyAsync(b => b.BranchId == branchId);
        }

        public async Task<bool> CodeExistsAsync(string branchCode, int? excludeBranchId = null)
        {
            var query = _context.Branches.Where(b => b.BranchCode == branchCode);

            if (excludeBranchId.HasValue)
                query = query.Where(b => b.BranchId != excludeBranchId.Value);

            return await query.AnyAsync();
        }

        public async Task<Branch> AddAsync(Branch branch)
        {
            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();
            return branch;
        }

        public async Task<Branch> UpdateAsync(Branch branch)
        {
            _context.Branches.Update(branch);
            await _context.SaveChangesAsync();
            return branch;
        }

        public async Task DeleteAsync(int branchId)
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch != null)
            {
                _context.Branches.Remove(branch);
                await _context.SaveChangesAsync();
            }
        }

        public async Task SoftDeleteAsync(int branchId)
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch != null)
            {
                branch.IsActive = false;
                branch.ModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }
    }
}
