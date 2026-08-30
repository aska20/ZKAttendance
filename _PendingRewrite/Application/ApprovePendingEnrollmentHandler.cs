using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Domain.Enums;

namespace ZKAttendance.Application.Enrollment;

public record ApproveEnrollmentCommand(
    int PendingEnrollmentId,
    string EmployeeCode,
    string FullName,
    int? DepartmentId,
    int? ShiftId,
    int? BranchId,
    DateTime? HireDateAd,
    string ApprovedBy);

public record ApproveEnrollmentResult(bool Success, int? EmployeeId, string? Error);

public interface IApprovePendingEnrollment
{
    Task<ApproveEnrollmentResult> ExecuteAsync(ApproveEnrollmentCommand cmd, CancellationToken ct = default);
    Task<bool> RejectAsync(int pendingEnrollmentId, string reason, string reviewedBy, CancellationToken ct = default);
}

/// <summary>
/// Turns a discovered device user into a real employee.
///
/// WHAT THIS DOES, IN ORDER
/// ------------------------
/// 1. Creates the Employee from the details HR typed in.
/// 2. Creates the mapping for the MASTER device, using the enrol number the
///    master already assigned. Nothing is pushed to the master - the person is
///    already on it, that is where they were enrolled.
/// 3. Moves the staged templates onto the new EmployeeId.
/// 4. Creates a mapping row for every active slave, allocating a free enrol
///    number on each one, and leaves TemplateSyncedUtc null.
///
/// Step 4 does NOT talk to any device. It only records intent. The propagation
/// job picks up every mapping with TemplateSyncedUtc == null and does the
/// pushing in the background.
///
/// WHY SPLIT IT THAT WAY
/// ---------------------
/// Approval is a button in a web request. If it tried to reach four devices
/// inline, HR would sit watching a spinner, and one unreachable branch machine
/// would fail the whole approval - leaving an employee who exists on the master
/// but not in the database. Recording intent and pushing later means approval
/// always succeeds in milliseconds, and an offline device simply gets its push
/// on the next run.
/// </summary>
public class ApprovePendingEnrollmentHandler : IApprovePendingEnrollment
{
    private readonly IPendingEnrollmentRepository _pending;
    private readonly IEmployeeRepository _employees;
    private readonly IDeviceRepository _devices;
    private readonly IMappingRepository _mappings;
    private readonly ITemplateRepository _templates;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly INepaliCalendar _nepali;
    private readonly ILogger<ApprovePendingEnrollmentHandler> _log;

    public ApprovePendingEnrollmentHandler(
        IPendingEnrollmentRepository pending,
        IEmployeeRepository employees,
        IDeviceRepository devices,
        IMappingRepository mappings,
        ITemplateRepository templates,
        IUnitOfWork uow,
        IClock clock,
        INepaliCalendar nepali,
        ILogger<ApprovePendingEnrollmentHandler> log)
    {
        _pending = pending;
        _employees = employees;
        _devices = devices;
        _mappings = mappings;
        _templates = templates;
        _uow = uow;
        _clock = clock;
        _nepali = nepali;
        _log = log;
    }

    public async Task<ApproveEnrollmentResult> ExecuteAsync(
        ApproveEnrollmentCommand cmd, CancellationToken ct = default)
    {
        var pending = await _pending.GetAsync(cmd.PendingEnrollmentId, ct);

        if (pending is null)
            return new ApproveEnrollmentResult(false, null, "Enrolment not found");

        if (pending.Status != EnrollmentStatus.AwaitingReview)
            return new ApproveEnrollmentResult(false, null,
                $"Enrolment has already been {pending.Status.ToString().ToLowerInvariant()}");

        if (await _employees.CodeExistsAsync(cmd.EmployeeCode, ct))
            return new ApproveEnrollmentResult(false, null,
                $"Employee code '{cmd.EmployeeCode}' is already in use");

        Employee? created = null;

        // One transaction. A half-approved enrolment - employee created but no
        // mapping - would show up as a person who exists but whose punches
        // never resolve.
        await _uow.ExecuteInTransactionAsync(async () =>
        {
            var employee = new Employee
            {
                EmployeeCode = cmd.EmployeeCode.Trim(),
                FullName = cmd.FullName.Trim(),
                DepartmentId = cmd.DepartmentId,
                ShiftId = cmd.ShiftId,
                BranchId = cmd.BranchId,
                HireDateAd = cmd.HireDateAd,
                HireDateBs = cmd.HireDateAd.HasValue ? _nepali.ToBsString(cmd.HireDateAd.Value) : null,
                MasterDeviceUserId = pending.DeviceUserId,
                IsActive = true
            };

            await _employees.AddAsync(employee, ct);
            await _uow.SaveChangesAsync(ct);   // need the identity value
            created = employee;

            // ── Master mapping: already enrolled, nothing to push ──
            await _mappings.AddAsync(new EmployeeDeviceMapping
            {
                EmployeeId = employee.EmployeeId,
                DeviceId = pending.DeviceId,
                DeviceUserId = pending.DeviceUserId,
                IsEnrolled = true,
                TemplateSyncedUtc = _clock.UtcNow,
                IsActive = true,
                CreatedUtc = _clock.UtcNow
            }, ct);

            // ── Move staged templates onto the real employee ──
            var staged = await PendingTemplateStore.TakeAsync(pending.DeviceId, pending.DeviceUserId, ct);

            if (staged.Count == 0)
                throw new InvalidOperationException(
                    "No fingerprint template was captured for this enrolment. " +
                    "Re-enrol the finger on the master device and pull again.");

            foreach (var t in staged) t.EmployeeId = employee.EmployeeId;
            await _templates.ReplaceForEmployeeAsync(employee.EmployeeId, staged, ct);

            // ── Queue every slave ──
            await CreateSlaveMappingsAsync(employee.EmployeeId, pending.DeviceId, ct);

            pending.Status = EnrollmentStatus.Approved;
            pending.ApprovedEmployeeId = employee.EmployeeId;
            pending.ReviewedUtc = _clock.UtcNow;
            pending.ReviewedBy = cmd.ApprovedBy;

            await _uow.SaveChangesAsync(ct);
        }, ct);

        _log.LogInformation(
            "Approved enrolment {Pending} as employee {EmployeeId} ({Name}); " +
            "slave devices queued for template push",
            cmd.PendingEnrollmentId, created!.EmployeeId, created.FullName);

        return new ApproveEnrollmentResult(true, created.EmployeeId, null);
    }

    /// <summary>
    /// Creates a mapping row per slave with a free enrol number on that device.
    ///
    /// The number is not assumed to match the master's. Trying to reuse 1017
    /// everywhere is what breaks the moment a branch device already gave 1017
    /// to somebody else - the push would overwrite a different person's
    /// fingerprint, and from then on two people would authenticate as one.
    /// </summary>
    private async Task CreateSlaveMappingsAsync(int employeeId, int masterDeviceId, CancellationToken ct)
    {
        var master = await _mappings.FindAsync(employeeId, masterDeviceId, ct);
        var preferred = master?.DeviceUserId ?? string.Empty;

        foreach (var slave in await _devices.GetActiveSlavesAsync(ct))
        {
            if (slave.DeviceId == masterDeviceId) continue;

            var used = await _mappings.GetUsedDeviceUserIdsAsync(slave.DeviceId, ct);
            var deviceUserId = AllocateDeviceUserId(preferred, used);

            await _mappings.AddAsync(new EmployeeDeviceMapping
            {
                EmployeeId = employeeId,
                DeviceId = slave.DeviceId,
                DeviceUserId = deviceUserId,
                IsEnrolled = false,
                TemplateSyncedUtc = null,   // this is what the push job looks for
                IsActive = true,
                CreatedUtc = _clock.UtcNow
            }, ct);

            if (deviceUserId != preferred)
                _log.LogInformation(
                    "Employee {EmployeeId} gets id {Allocated} on {Device} " +
                    "because {Preferred} is already taken there",
                    employeeId, deviceUserId, slave.DeviceName, preferred);
        }
    }

    /// <summary>
    /// Keep the master's number when it is free on this device - it makes
    /// support far easier when the same person is 1017 everywhere. Otherwise
    /// take the next unused number.
    /// </summary>
    internal static string AllocateDeviceUserId(string preferred, HashSet<string> used)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && !used.Contains(preferred))
            return preferred;

        var highest = used
            .Select(u => int.TryParse(u, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return (highest + 1).ToString();
    }

    public async Task<bool> RejectAsync(
        int pendingEnrollmentId, string reason, string reviewedBy, CancellationToken ct = default)
    {
        var pending = await _pending.GetAsync(pendingEnrollmentId, ct);
        if (pending is null || pending.Status != EnrollmentStatus.AwaitingReview) return false;

        // Kept on record rather than deleted, so the next pull does not
        // rediscover the same test enrolment and raise it again.
        pending.Status = EnrollmentStatus.Rejected;
        pending.RejectionReason = reason;
        pending.ReviewedUtc = _clock.UtcNow;
        pending.ReviewedBy = reviewedBy;

        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
