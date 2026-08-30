using Microsoft.EntityFrameworkCore;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Infrastructure.Services.Attendances
{
    public class ManualAttendanceEntry : IManualAttendanceEntry
    {
        private readonly AttendanceDbContext _context;
        private readonly ILogger<ManualAttendanceEntry> _logger;

        public ManualAttendanceEntry(AttendanceDbContext context, ILogger<ManualAttendanceEntry> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<AttendanceLog> AddAsync(
            int employeeId,
            int deviceId,
            DateTime punchTime,
            string attendanceType,
            string? notes,
            CancellationToken ct = default)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId, ct)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found");

            var device = await _context.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct)
                ?? throw new InvalidOperationException($"Device {deviceId} not found");

            // Use the number this employee holds ON THIS DEVICE if one is
            // recorded, otherwise their primary. The unique index is on
            // (BiometricUserId, AttendanceTime, DeviceId), so getting this
            // wrong would let a manual row slip past the duplicate guard.
            var perDevice = await _context.EmployeeDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(ed => ed.EmployeeId == employeeId
                                        && ed.DeviceId == deviceId, ct);

            var biometricId = perDevice?.BiometricUserId ?? employee.BiometricUserId;

            var duplicate = await _context.AttendanceLogs.AnyAsync(
                a => a.DeviceId == deviceId
                  && a.BiometricUserId == biometricId
                  && a.AttendanceTime == punchTime, ct);

            if (duplicate)
                throw new InvalidOperationException(
                    "A punch already exists for this employee on this device at that exact time.");

            var log = new AttendanceLog
            {
                EmployeeId = employeeId,
                BiometricUserId = biometricId,
                DeviceId = deviceId,
                BranchId = device.BranchId,
                AttendanceTime = punchTime,      // stored in AD, exactly as given
                AttendanceType = attendanceType,
                VerifyMethod = "Manual",
                IsManual = true,                 // the audit flag
                IsSynced = false,
                IsProcessed = false,
                Notes = notes,
                CreatedDate = DateTime.Now
            };

            _context.AttendanceLogs.Add(log);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Manual punch {LogId} added for employee {EmployeeId} on device {DeviceId}",
                log.LogId, employeeId, deviceId);

            return log;
        }
    }
}
