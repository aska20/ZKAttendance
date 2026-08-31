using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Infrastructure.Security
{
    /// <summary>
    /// Keeps a login account (<see cref="ApiUser"/>) and the HR record
    /// (<see cref="Employee"/>) pointing at the same person.
    ///
    /// Without this the two are created independently: an employee added from
    /// the Employees screen has no way to sign in, and someone who registers a
    /// login from the sign-in page is not connected to any attendance data. The
    /// link is made on a best-effort match by biometric/enrol number first,
    /// then by email.
    /// </summary>
    public class EmployeeAccountLinker
    {
        private readonly AttendanceDbContext _db;

        public EmployeeAccountLinker(AttendanceDbContext db) => _db = db;

        /// <summary>
        /// Finds the Employee a new account should belong to. Returns null when
        /// nothing matches, in which case the account simply stands alone.
        /// </summary>
        public async Task<int?> ResolveEmployeeIdAsync(
            string? email,
            string? biometricUserId,
            CancellationToken ct = default)
        {
            biometricUserId = biometricUserId?.Trim();
            email = email?.Trim();

            Employee? match = null;

            if (!string.IsNullOrWhiteSpace(biometricUserId))
            {
                match = await _db.Employees
                    .FirstOrDefaultAsync(e => e.BiometricUserId == biometricUserId, ct);
            }

            if (match is null && !string.IsNullOrWhiteSpace(email))
            {
                var lowered = email.ToLower();
                match = await _db.Employees
                    .FirstOrDefaultAsync(e => e.Email != null && e.Email.ToLower() == lowered, ct);
            }

            return match?.EmployeeId;
        }

        /// <summary>
        /// After an employee is saved, attach any orphan login account whose
        /// email matches, so a person added from the Employees screen who had
        /// already registered a login becomes one linked entity.
        /// </summary>
        public async Task BackfillAccountForEmployeeAsync(Employee employee, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(employee.Email))
                return;

            var lowered = employee.Email.Trim().ToLower();

            var orphan = await _db.ApiUsers
                .FirstOrDefaultAsync(u => u.EmployeeId == null && u.Email.ToLower() == lowered, ct);

            if (orphan is null)
                return;

            orphan.EmployeeId = employee.EmployeeId;
            await _db.SaveChangesAsync(ct);
        }
    }
}
