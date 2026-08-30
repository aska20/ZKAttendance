using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Application.Dtos.Reports;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Services.Attendances;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Attendances
{
    public class AttendanceService : IAttendanceService
    {
        private readonly AttendanceLogRepository _repository;
        private readonly EmployeeRepository _employeeRepository;
        private readonly ILogger<AttendanceService> _logger;

        public AttendanceService(
            AttendanceLogRepository repository,
            EmployeeRepository employeeRepository,
            ILogger<AttendanceService> logger)
        {
            _repository = repository;
            _employeeRepository = employeeRepository;
            _logger = logger;
        }

        public async Task<List<AttendanceLog>> GetAllAttendanceLogsAsync()
        {
            try
            {
                var today = DateTime.Today;
                var lastMonth = today.AddMonths(-1);
                return await _repository.GetByDateRangeAsync(lastMonth, today.AddDays(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching attendance logs");
                throw;
            }
        }

        public async Task<List<AttendanceLog>> GetAttendanceLogsByDateRangeAsync(
            DateTime? startDate,
            DateTime? endDate)
        {
            try
            {
                var from = startDate ?? DateTime.Today.AddMonths(-1);
                var to = (endDate ?? DateTime.Today).AddDays(1);

                return await _repository.GetByDateRangeAsync(from, to);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching attendance logs from {StartDate} To {EndDate}", startDate, endDate);
                throw;
            }
        }

        public async Task<List<DailyAttendanceItemDto>> GetDailyAttendanceReportAsync(
    DateTime date,
    int? branchId = null,
    int? departmentId = null)
        {
            try
            {
                var startDate = date.Date;
                var endDate = startDate.AddDays(1);

                var logs = await _repository.GetByDateRangeAsync(startDate, endDate, null, branchId);

                var grouped = logs
                    .GroupBy(l => l.BiometricUserId)
                    .Select(g => new DailyAttendanceItemDto  
                    {
                        EmployeeId = g.First().Employee?.EmployeeId ?? 0,  
                        BiometricUserId = g.Key,
                        EmployeeName = g.First().Employee?.EmployeeName ?? "Unknown",
                        Department = g.First().Employee?.Department?.DepartmentName ?? "-",  
                        FirstCheckIn = g.OrderBy(l => l.AttendanceTime).FirstOrDefault()?.AttendanceTime,  
                        LastCheckOut = g.OrderByDescending(l => l.AttendanceTime).FirstOrDefault()?.AttendanceTime,  
                        Status = "Present"  // TODO: derive from shift rules
                    })
                    .OrderBy(r => r.EmployeeName)
                    .ToList();

                // Calculate TotalWorkHours
                foreach (var item in grouped)
                {
                    if (item.FirstCheckIn.HasValue && item.LastCheckOut.HasValue)
                    {
                        item.TotalWorkHours = item.LastCheckOut.Value - item.FirstCheckIn.Value;
                    }
                }

                return grouped;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building the daily attendance report for {Date}", date);
                throw;
            }
        }


        public async Task<DailyAttendanceReportSummaryDto> GetDailyAttendanceReportSummaryAsync(
    DateTime date,
    int? branchId = null,        // accepted but not used yet
    int? departmentId = null)
        {
            try
            {
                var startDate = date.Date;
                var endDate = startDate.AddDays(1);

                // 1. Load all active employees, filtered by department only
                var allEmployees = await _employeeRepository.GetFilteredEmployeesAsync(departmentId);

                // 2. Load that day's attendance logs
                var attendanceLogs = await _repository.GetByDateRangeAsync(startDate, endDate, null, branchId);

                // 3. Work out who was present
                var presentEmployeeIds = attendanceLogs
                    .Select(l => l.BiometricUserId)
                    .Distinct()
                    .ToList();

                // 4. Build the present list
                var presentEmployees = new List<PresentEmployeeDto>();
                foreach (var biometricId in presentEmployeeIds)
                {
                    var employee = allEmployees.FirstOrDefault(e => e.BiometricUserId == biometricId);
                    if (employee == null) continue;

                    var employeeLogs = attendanceLogs
                        .Where(l => l.BiometricUserId == biometricId)
                        .OrderBy(l => l.AttendanceTime)
                        .ToList();

                    var checkIn = employeeLogs.FirstOrDefault()?.AttendanceTime;
                    var checkOut = employeeLogs.LastOrDefault()?.AttendanceTime;

                    TimeSpan? workDuration = null;
                    if (checkIn.HasValue && checkOut.HasValue && checkOut > checkIn)
                    {
                        workDuration = checkOut.Value - checkIn.Value;
                    }

                    // Decide the status
                    bool isLate = false;
                    bool isEarlyLeave = false;
                    string status = "On Time";

                    if (checkIn.HasValue && checkIn.Value.TimeOfDay > new TimeSpan(8, 0, 0))
                    {
                        isLate = true;
                        status = "Late";
                    }

                    presentEmployees.Add(new PresentEmployeeDto
                    {
                        EmployeeId = employee.EmployeeId,
                        EmployeeNumber = employee.SSN ?? "-",  // uses SSN instead of EmployeeNumber
                        EmployeeName = employee.EmployeeName,
                        BiometricUserId = employee.BiometricUserId,
                        DepartmentName = employee.Department?.DepartmentName ?? "-",
                        CheckInTime = checkIn,
                        CheckOutTime = checkOut,
                        WorkDuration = workDuration,
                        Status = status,
                        IsLate = isLate,
                        IsEarlyLeave = isEarlyLeave
                    });
                }

                // 5. Build the absent list
                var absentEmployees = allEmployees
                    .Where(e => !presentEmployeeIds.Contains(e.BiometricUserId))
                    .Select(e => new AbsentEmployeeDto
                    {
                        EmployeeId = e.EmployeeId,
                        EmployeeNumber = e.SSN ?? "-",  // uses SSN
                        EmployeeName = e.EmployeeName,
                        BiometricUserId = e.BiometricUserId,
                        DepartmentName = e.Department?.DepartmentName ?? "-",
                        PhoneNumber = e.PhoneNumber,
                        AbsentReason = "Absent without notice"
                    })
                    .ToList();

                // 6. Build the final summary
                var summary = new DailyAttendanceReportSummaryDto
                {
                    ReportDate = date,
                    TotalEmployees = allEmployees.Count,
                    PresentCount = presentEmployees.Count,
                    AbsentCount = absentEmployees.Count,
                    LateCount = presentEmployees.Count(p => p.IsLate),
                    EarlyLeaveCount = presentEmployees.Count(p => p.IsEarlyLeave),
                    PresentEmployees = presentEmployees.OrderBy(p => p.EmployeeName).ToList(),
                    AbsentEmployees = absentEmployees.OrderBy(a => a.EmployeeName).ToList()
                };

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building the attendance summary for {Date}", date);
                throw;
            }
        }

    }
}
