using ZKAttendance.Application.Dtos.Reports;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Application.Abstractions
{
    public interface IAttendanceService
    {
        Task<List<AttendanceLog>> GetAllAttendanceLogsAsync();
        Task<List<AttendanceLog>> GetAttendanceLogsByDateRangeAsync(DateTime? startDate, DateTime? endDate);
        Task<List<DailyAttendanceItemDto>> GetDailyAttendanceReportAsync(
              DateTime date,
              int? branchId = null,
              int? departmentId = null);

        Task<DailyAttendanceReportSummaryDto> GetDailyAttendanceReportSummaryAsync(
            DateTime date,
            int? branchId = null,
            int? departmentId = null);
    }
}
