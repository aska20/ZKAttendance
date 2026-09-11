using Microsoft.EntityFrameworkCore;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Infrastructure.Services.Attendance
{
    /// <summary>The hours that apply to one employee, and where they came from.</summary>
    public record EffectiveShift(
        TimeSpan StartTime,
        TimeSpan EndTime,
        int GraceMinutes,
        /// <summary>"Employee", "Department" or "Settings". Shown in the UI so
        /// nobody has to guess why someone is being marked late.</summary>
        string Source,
        int? ShiftId,
        string? ShiftName);

    public interface IShiftResolver
    {
        /// <summary>Resolve for one employee.</summary>
        Task<EffectiveShift> ResolveAsync(int employeeId, CancellationToken ct = default);

        /// <summary>
        /// Resolve for many at once. The grid asks for hundreds of employees per
        /// request, so doing this one at a time would be hundreds of queries.
        /// </summary>
        Task<Dictionary<int, EffectiveShift>> ResolveManyAsync(
            IEnumerable<int> employeeIds, CancellationToken ct = default);
    }

    /// <summary>
    /// Works out which hours apply to a person, most specific first:
    ///
    ///     1. the employee's own shift        DefaultShiftId on Employee
    ///     2. their department's shift        DefaultShiftId on Department
    ///     3. the office hours in Settings    AttendancePolicy
    ///
    /// The individual override wins on purpose. There is always one person who
    /// starts early, and forcing them onto the department's hours would mark
    /// them late every day.
    ///
    /// Putting this in one place matters: before, the grid and the daily report
    /// each read DefaultShiftId themselves and neither knew about departments.
    /// </summary>
    public class ShiftResolver : IShiftResolver
    {
        private readonly AttendanceDbContext _db;
        private readonly IAttendancePolicyService _policy;

        public ShiftResolver(AttendanceDbContext db, IAttendancePolicyService policy)
        {
            _db = db;
            _policy = policy;
        }

        public async Task<EffectiveShift> ResolveAsync(int employeeId, CancellationToken ct = default)
        {
            var map = await ResolveManyAsync(new[] { employeeId }, ct);
            return map[employeeId];
        }

        public async Task<Dictionary<int, EffectiveShift>> ResolveManyAsync(
            IEnumerable<int> employeeIds, CancellationToken ct = default)
        {
            var ids = employeeIds.Distinct().ToList();
            var result = new Dictionary<int, EffectiveShift>();
            if (ids.Count == 0) return result;

            var policy = await _policy.GetAsync(ct);
            var fallback = new EffectiveShift(
                policy.OfficeStartTime, policy.OfficeEndTime, policy.GraceMinutes,
                "Settings", null, null);

            var employees = await _db.Employees
                .Where(e => ids.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.DefaultShiftId, e.DepartmentId })
                .ToListAsync(ct);

            // One query for the departments involved, not one per employee.
            var deptIds = employees.Where(e => e.DepartmentId.HasValue)
                                   .Select(e => e.DepartmentId!.Value)
                                   .Distinct().ToList();

            var deptShift = deptIds.Count == 0
                ? new Dictionary<int, int?>()
                : await _db.Departments
                    .Where(d => deptIds.Contains(d.DepartmentId))
                    .ToDictionaryAsync(d => d.DepartmentId, d => d.DefaultShiftId, ct);

            // And one for every shift either level might point at.
            var shiftIds = employees.Select(e => e.DefaultShiftId)
                .Concat(deptShift.Values)
                .Where(x => x.HasValue).Select(x => x!.Value)
                .Distinct().ToList();

            var shifts = shiftIds.Count == 0
                ? new Dictionary<int, WorkShift>()
                : await _db.WorkShifts
                    .Where(s => shiftIds.Contains(s.ShiftId))
                    .ToDictionaryAsync(s => s.ShiftId, ct);

            foreach (var id in ids)
            {
                var e = employees.FirstOrDefault(x => x.EmployeeId == id);
                if (e is null) { result[id] = fallback; continue; }

                // 1. their own shift
                if (e.DefaultShiftId is { } own && shifts.TryGetValue(own, out var ownShift))
                {
                    result[id] = From(ownShift, "Employee");
                    continue;
                }

                // 2. their department's shift
                if (e.DepartmentId is { } dept
                    && deptShift.TryGetValue(dept, out var deptShiftId)
                    && deptShiftId is { } ds
                    && shifts.TryGetValue(ds, out var departmentShift))
                {
                    result[id] = From(departmentShift, "Department");
                    continue;
                }

                // 3. office hours
                result[id] = fallback;
            }

            return result;

            static EffectiveShift From(WorkShift s, string source) =>
                new(s.StartTime, s.EndTime, s.LateMinutes, source, s.ShiftId, s.ShiftName);
        }
    }
}
