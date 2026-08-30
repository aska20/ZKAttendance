using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    public interface IWorkShiftService
    {
        Task<List<WorkShift>> GetAllShiftsAsync();
        Task<WorkShift?> GetShiftByIdAsync(int shiftId);
        Task<WorkShift> CreateShiftAsync(WorkShift shift);
        Task<WorkShift> UpdateShiftAsync(WorkShift shift);
        Task DeleteShiftAsync(int shiftId);
        Task<bool> CanDeleteShiftAsync(int shiftId);
        Task<List<WorkShift>> GetActiveShiftsForTimeAsync(TimeSpan currentTime);
    }
}
