using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Dtos;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Application.Services.Attendances
{

    // Responsible for: check-in/check-out, working hours, and building AttendanceViewModel.


    public class AttendanceCalculationService
    {
        // Injected as a PORT, not called as an extension method on DateTime.
        //
        // The converter itself lives in Infrastructure, because it is an
        // adapter over a published calendar table. If this class called
        // .ToBsString() directly it would need a using for Infrastructure,
        // and Application would depend on an outer layer - the exact rule
        // Clean Architecture exists to prevent.
        private readonly INepaliCalendar _nepali;

        public AttendanceCalculationService(INepaliCalendar nepali)
        {
            _nepali = nepali;
        }

        /// <param name="employees">Keyed by EmployeeId, not by biometric ID.</param>
        public Task<List<AttendanceViewModel>> BuildAttendanceViewModels(
            List<AttendanceLog> logs,
            Dictionary<int, Employee> employees,
            Dictionary<int, string> branches,
            Dictionary<int, string> devices)
        {
            // CHANGED: grouped by EmployeeId, previously by BiometricUserId.
            //
            // Once an employee can be 1017 on the head office device and 88 on
            // the branch device, grouping by biometric ID splits one working
            // day into two rows. Grouping by employee keeps a check-in at one
            // office and a check-out at another in the same record. The device
            // is deliberately NOT part of the key - that is what makes
            // multi-device collection work without any special-case code.
            var groupedLogs = logs
                .Where(l => l.EmployeeId.HasValue)      // unmapped punches are shown separately
                .GroupBy(l => new { EmployeeId = l.EmployeeId!.Value, Date = l.AttendanceTime.Date })
                .Select(g => new
                {
                    EmployeeId = g.Key.EmployeeId,
                    Date = g.Key.Date,
                    Logs = g.ToList()
                })
                .ToList();

            var viewModels = new List<AttendanceViewModel>();

            foreach (var group in groupedLogs)
            {
                if (!employees.TryGetValue(group.EmployeeId, out var emp))
                    continue;

                var checkIn = group.Logs.OrderBy(x => x.AttendanceTime).FirstOrDefault();
                var checkOut = group.Logs.Count > 1
                    ? group.Logs.OrderByDescending(x => x.AttendanceTime).FirstOrDefault()
                    : null;

                double workingHours = CalculateWorkingHours(checkIn, checkOut);
                var status = GetAttendanceStatus(checkIn, checkOut, workingHours);

                // NEW: keep BOTH devices. The check-in and check-out can come
                // from different machines, and showing only one of them hides
                // that the employee moved between branches during the day.
                var inDevice = checkIn != null && devices.ContainsKey(checkIn.DeviceId)
                    ? devices[checkIn.DeviceId] : "-";
                var outDevice = checkOut != null && devices.ContainsKey(checkOut.DeviceId)
                    ? devices[checkOut.DeviceId] : "-";

                viewModels.Add(new AttendanceViewModel
                {
                    EmployeeId = emp.EmployeeId,
                    BiometricUserId = emp.BiometricUserId,
                    EmployeeName = emp.EmployeeName,
                    Date = group.Date,
                    NepaliDate = _nepali.ToBsString(group.Date),
                    CheckInTime = checkIn?.AttendanceTime,
                    CheckOutTime = checkOut?.AttendanceTime,
                    BranchName = checkIn != null && branches.ContainsKey(checkIn.BranchId)
                        ? branches[checkIn.BranchId] : "-",
                    DeviceName = inDevice,
                    CheckInDeviceName = inDevice,
                    CheckOutDeviceName = outDevice,
                    IsCrossDevice = checkIn != null && checkOut != null
                                    && checkIn.DeviceId != checkOut.DeviceId,
                    WorkingHours = workingHours,
                    Status = status,
                });
            }

            return Task.FromResult(viewModels);
        }

        private double CalculateWorkingHours(AttendanceLog? checkIn, AttendanceLog? checkOut)
        {
            if (checkIn == null || checkOut == null)
                return 0;

            var timeDiff = (checkOut.AttendanceTime - checkIn.AttendanceTime).TotalMinutes;
            return timeDiff < 30 ? 0 : (checkOut.AttendanceTime - checkIn.AttendanceTime).TotalHours;
        }

        private string GetAttendanceStatus(AttendanceLog? checkIn, AttendanceLog? checkOut, double workingHours)
        {
            if (checkIn == null && checkOut != null)
                return "Check-out Only";

            if (checkIn == null)
                return "Absent";

            if (checkOut == null || workingHours == 0)
                return "Present";

            if (workingHours < 4)
                return "Half Day";

            return "Full Day";
        }
    }
}
