using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.WorkShifts
{
    public class WorkShiftService : IWorkShiftService
    {
        private readonly WorkShiftRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<WorkShiftService> _logger;

        public WorkShiftService(
            WorkShiftRepository repository,
            IMemoryCache cache,
            ILogger<WorkShiftService> logger)
        {
            _repository = repository;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<WorkShift>> GetAllShiftsAsync()
        {
            try
            {
                return await _repository.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the shift list");
                throw;
            }
        }

        public async Task<WorkShift?> GetShiftByIdAsync(int shiftId)
        {
            try
            {
                return await _repository.GetByIdAsync(shiftId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching the shift {ShiftId}", shiftId);
                throw;
            }
        }

        public async Task<WorkShift> CreateShiftAsync(WorkShift shift)
        {
            try
            {
                // Reject a duplicate name
                if (await _repository.NameExistsAsync(shift.ShiftName))
                {
                    throw new InvalidOperationException(
                        $"Shift Name '{shift.ShiftName}' already exists");
                }

                // Validate the times
                ValidateShiftTimes(shift);

                shift.IsActive = true;
                shift.CreatedDate = DateTime.Now;

                var result = await _repository.AddAsync(shift);

                ClearShiftCache();
                _logger.LogInformation("Created shift: {ShiftName}", shift.ShiftName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating shift: {ShiftName}", shift.ShiftName);
                throw;
            }
        }

        public async Task<WorkShift> UpdateShiftAsync(WorkShift shift)
        {
            try
            {
                if (!await _repository.ExistsAsync(shift.ShiftId))
                {
                    throw new InvalidOperationException("Shift not found");
                }

                // Reject a duplicate name
                if (await _repository.NameExistsAsync(shift.ShiftName, shift.ShiftId))
                {
                    throw new InvalidOperationException(
                        $"Shift Name '{shift.ShiftName}' already exists");
                }

                // Validate the times
                ValidateShiftTimes(shift);

                shift.ModifiedDate = DateTime.Now;
                var result = await _repository.UpdateAsync(shift);

                ClearShiftCache();
                _logger.LogInformation("Updated shift: {ShiftName}", shift.ShiftName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating shift {ShiftId}", shift.ShiftId);
                throw;
            }
        }

        public async Task DeleteShiftAsync(int shiftId)
        {
            try
            {
                // Make sure no employee is linked to this shift
                var employeeCount = await _repository.GetEmployeeCountAsync(shiftId);
                if (employeeCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete the shift because it is linked to {employeeCount} employee");
                }

                await _repository.SoftDeleteAsync(shiftId);

                ClearShiftCache();
                _logger.LogInformation("Shift deleted {ShiftId}", shiftId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting shift {ShiftId}", shiftId);
                throw;
            }
        }

        public async Task<bool> CanDeleteShiftAsync(int shiftId)
        {
            try
            {
                var employeeCount = await _repository.GetEmployeeCountAsync(shiftId);
                return employeeCount == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking whether the shift can be deleted {ShiftId}", shiftId);
                throw;
            }
        }

        public async Task<List<WorkShift>> GetActiveShiftsForTimeAsync(TimeSpan currentTime)
        {
            try
            {
                return await _repository.GetActiveShiftsForTimeAsync(currentTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active shifts for the time {Time}", currentTime);
                throw;
            }
        }

        private void ValidateShiftTimes(WorkShift shift)
        {
            // CheckInWindowStart must precede StartTime
            if (shift.CheckInWindowStart.HasValue && shift.CheckInWindowStart.Value > shift.StartTime)
            {
                throw new InvalidOperationException(
                    "The check-in window must start before the shift start time");
            }

            // CheckOutWindowEnd must follow EndTime
            if (shift.CheckOutWindowEnd.HasValue && shift.CheckOutWindowEnd.Value < shift.EndTime)
            {
                throw new InvalidOperationException(
                    "The check-out window must end after the shift end time");
            }

            // Validate break time
            if (shift.BreakMinutes < 0)
            {
                throw new InvalidOperationException("Break time cannot be negative");
            }

            // Validate late/early minutes
            if (shift.LateMinutes < 0 || shift.EarlyMinutes < 0)
            {
                throw new InvalidOperationException("Late and early-departure minutes cannot be negative");
            }
        }

        private void ClearShiftCache()
        {
            _cache.Remove("active_shifts");
            _cache.Remove("shifts");
        }
    }
}
