using ZKAttendance.Application.Dtos.Reports;

namespace ZKAttendance.Application.Abstractions
{
    public interface IReportService
    {
        Task<DailyAttendanceReportDto> GetDailyAttendanceReportAsync(DateTime date, int? branchId = null, string? department = null);

        // Signature takes device and employee filters instead of a department string
        Task<DailyAttendanceReportDto> GetDailyAttendanceRangeReportAsync(
            DateTime fromDate,
            DateTime toDate,
            int? branchId = null,
            int? deviceId = null,
            int? employeeId = null);
    }
}
