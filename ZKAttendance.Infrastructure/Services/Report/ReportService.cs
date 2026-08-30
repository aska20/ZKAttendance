using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Application.Dtos.Reports;

using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;

namespace ZKAttendance.Infrastructure.Services.Report
{
    public class ReportService : IReportService
    {
        private readonly AttendanceDbContext _context;

        public ReportService(AttendanceDbContext context)
        {
            _context = context;
        }

        public async Task<DailyAttendanceReportDto> GetDailyAttendanceReportAsync(DateTime date, int? branchId = null, string? department = null)
        {
            var startDate = date.Date;
            var endDate = date.Date.AddDays(1);

            var employeesQuery = _context.Employees
                .Include(e => e.Department)  // eager-load Department
                .Where(e => e.IsActive);

            // Department comparison fix:
            if (!string.IsNullOrEmpty(department))
            {
                employeesQuery = employeesQuery.Where(e =>
                    e.Department != null && e.Department.DepartmentName == department);
            }

            var allEmployees = await employeesQuery.ToListAsync();

            var attendanceLogsQuery = _context.AttendanceLogs
                .Where(a => a.AttendanceTime >= startDate && a.AttendanceTime < endDate);

            if (branchId.HasValue)
            {
                attendanceLogsQuery = attendanceLogsQuery.Where(a => a.BranchId == branchId.Value);
            }

            var attendanceLogs = await attendanceLogsQuery.ToListAsync();

            // Query fix:
            var groupedAttendance = attendanceLogs
                .GroupBy(a => a.BiometricUserId)
                .Select(g => new
                {
                    BiometricUserId = g.Key,
                    FirstCheckIn = g.Where(x => x.AttendanceType == "CheckIn" || x.AttendanceType == null)
                                    .OrderBy(x => x.AttendanceTime)
                                    .FirstOrDefault()?.AttendanceTime,
                    LastCheckOut = g.Where(x => x.AttendanceType == "CheckOut")
                                    .OrderByDescending(x => x.AttendanceTime)
                                    .FirstOrDefault()?.AttendanceTime,
                    FirstRecord = g.OrderBy(x => x.AttendanceTime).FirstOrDefault()?.AttendanceTime,
                    LastRecord = g.OrderByDescending(x => x.AttendanceTime).FirstOrDefault()?.AttendanceTime
                })
                .ToDictionary(x => x.BiometricUserId);

            var items = new List<DailyAttendanceItemDto>();

            foreach (var employee in allEmployees)
            {
                var item = new DailyAttendanceItemDto
                {
                    EmployeeId = employee.EmployeeId,
                    EmployeeName = employee.EmployeeName,
                    BiometricUserId = employee.BiometricUserId,
                    Department = employee.Department?.DepartmentName ?? "Not set"  
                };

                if (groupedAttendance.TryGetValue(employee.BiometricUserId, out var attendance))
                {
                    item.FirstCheckIn = attendance.FirstCheckIn ?? attendance.FirstRecord;
                    item.LastCheckOut = attendance.LastCheckOut ??
                                       (attendance.LastRecord != attendance.FirstRecord ? attendance.LastRecord : null);

                    if (item.FirstCheckIn.HasValue && item.LastCheckOut.HasValue)
                    {
                        item.TotalWorkHours = item.LastCheckOut.Value - item.FirstCheckIn.Value;
                        item.Status = "Present";
                    }
                    else if (item.FirstCheckIn.HasValue && !item.LastCheckOut.HasValue)
                    {
                        item.Status = "No check-out";
                    }
                    else
                    {
                        item.Status = "Absent";
                    }
                }
                else
                {
                    item.Status = "Absent";
                }

                items.Add(item);
            }

            var report = new DailyAttendanceReportDto
            {
                Date = date.Date,
                Items = items.OrderBy(i => i.EmployeeName).ToList(),
                TotalEmployees = allEmployees.Count,
                PresentCount = items.Count(i => i.Status == "Present" || i.Status == "No check-out"),
                AbsentCount = items.Count(i => i.Status == "Absent")
            };

            return report;
        }

        public async Task<DailyAttendanceReportDto> GetDailyAttendanceRangeReportAsync(DateTime fromDate, DateTime toDate, int? branchId = null, int? deviceId = null, int? employeeId = null)
        {
            var startDate = fromDate.Date;
            var endDate = toDate.Date.AddDays(1);

            var employeesQuery = _context.Employees
                .Include(e => e.Department)  // eager-load Department
                .Where(e => e.IsActive);

            if (employeeId.HasValue)
                employeesQuery = employeesQuery.Where(e => e.EmployeeId == employeeId.Value);

            var allEmployees = await employeesQuery.ToListAsync();

            var attendanceLogsQuery = _context.AttendanceLogs
                .Where(a => a.AttendanceTime >= startDate && a.AttendanceTime < endDate);

            if (branchId.HasValue)
                attendanceLogsQuery = attendanceLogsQuery.Where(a => a.BranchId == branchId.Value);

            if (deviceId.HasValue)
                attendanceLogsQuery = attendanceLogsQuery.Where(a => a.DeviceId == deviceId.Value);

            var attendanceLogs = await attendanceLogsQuery.ToListAsync();

            // Fix:
            var groupedAttendance = attendanceLogs
                .GroupBy(a => a.BiometricUserId)
                .Select(g => new
                {
                    BiometricUserId = g.Key,
                    FirstCheckIn = g.Where(x => x.AttendanceType == "CheckIn" || x.AttendanceType == null)
                                    .OrderBy(x => x.AttendanceTime)
                                    .FirstOrDefault()?.AttendanceTime,
                    LastCheckOut = g.Where(x => x.AttendanceType == "CheckOut")
                                    .OrderByDescending(x => x.AttendanceTime)
                                    .FirstOrDefault()?.AttendanceTime,
                    FirstRecord = g.OrderBy(x => x.AttendanceTime).FirstOrDefault()?.AttendanceTime,
                    LastRecord = g.OrderByDescending(x => x.AttendanceTime).FirstOrDefault()?.AttendanceTime
                })
                .ToDictionary(x => x.BiometricUserId);

            var items = new List<DailyAttendanceItemDto>();

            foreach (var employee in allEmployees)
            {
                if (employeeId.HasValue && employee.EmployeeId != employeeId.Value)
                    continue;

                var item = new DailyAttendanceItemDto
                {
                    EmployeeId = employee.EmployeeId,
                    EmployeeName = employee.EmployeeName,
                    BiometricUserId = employee.BiometricUserId,
                    Department = employee.Department?.DepartmentName ?? "Not set"  
                };

                if (groupedAttendance.TryGetValue(employee.BiometricUserId, out var attendance))
                {
                    item.FirstCheckIn = attendance.FirstCheckIn ?? attendance.FirstRecord;
                    item.LastCheckOut = attendance.LastCheckOut ??
                                       (attendance.LastRecord != attendance.FirstRecord ? attendance.LastRecord : null);

                    if (item.FirstCheckIn.HasValue && item.LastCheckOut.HasValue)
                    {
                        item.TotalWorkHours = item.LastCheckOut.Value - item.FirstCheckIn.Value;
                        item.Status = "Present";
                    }
                    else if (item.FirstCheckIn.HasValue && !item.LastCheckOut.HasValue)
                    {
                        item.Status = "No check-out";
                    }
                    else
                    {
                        item.Status = "Absent";
                    }
                }
                else
                {
                    item.Status = "Absent";
                }

                items.Add(item);
            }

            var report = new DailyAttendanceReportDto
            {
                Date = fromDate.Date,
                Items = items.OrderBy(i => i.EmployeeName).ToList(),
                TotalEmployees = allEmployees.Count,
                PresentCount = items.Count(i => i.Status == "Present" || i.Status == "No check-out"),
                AbsentCount = items.Count(i => i.Status == "Absent")
            };

            return report;
        }
    }
}
