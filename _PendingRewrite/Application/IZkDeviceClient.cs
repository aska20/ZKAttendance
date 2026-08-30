using ZKAttendance.Domain.Enums;

namespace ZKAttendance.Application.Abstractions;

public record DeviceUserRecord(
    string DeviceUserId,
    string Name,
    int Privilege,
    bool Enabled,
    string? Password,
    string? CardNumber);

public record DeviceTemplateRecord(
    string DeviceUserId,
    int FingerIndex,
    byte[] TemplateData,
    int FormatVersion);

public record DevicePunchRecord(
    string DeviceUserId,
    DateTime PunchTime,
    VerifyMode VerifyMode,
    PunchDirection Direction,
    int WorkCode);

public record DeviceCapabilities(
    string SerialNumber,
    string FirmwareVersion,
    int TemplateFormatVersion,
    int UserCapacity,
    int UserCount,
    int LogCount,
    DateTime DeviceTime);

/// <summary>
/// Everything the application needs from one physical ZKTeco machine.
///
/// WHY THIS IS AN INTERFACE IN THE APPLICATION LAYER
/// -------------------------------------------------
/// This is the port; Infrastructure supplies the adapter. The real adapter
/// wraps zkemkeeper.dll, a 32-bit COM component that needs regsvr32, forces
/// the host project to target x86, and cannot run in a test or a CI build.
/// If the enrolment and provisioning logic referenced that type directly,
/// none of it could be tested or demonstrated without hardware on the LAN.
///
/// Two adapters exist: ZkemkeeperDeviceClient (real) and FakeZkDeviceClient
/// (simulates a master plus slaves in memory).
///
/// DIRECTION OF EACH CALL
/// ----------------------
/// PULL  (device -> application):  GetUsersAsync, GetTemplatesAsync, GetPunchesAsync
/// PUSH  (application -> device):  UpsertUserAsync, WriteTemplateAsync, DeleteUserAsync
///
/// Devices never call each other. Every arrow passes through the application,
/// which is the only component that holds all the IP addresses.
/// </summary>
public interface IZkDeviceClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default);
    Task DisconnectAsync();

    Task<DeviceCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>Align the device clock to the server before reading punches.</summary>
    Task<bool> SetDeviceTimeAsync(DateTime serverTime, CancellationToken ct = default);

    // ── PULL ────────────────────────────────────────────────────────────

    /// <summary>Every user enrolled on the device.</summary>
    Task<IReadOnlyList<DeviceUserRecord>> GetUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// All fingerprint templates for one user. A person may have several
    /// fingers enrolled and all of them must travel together, otherwise the
    /// slave device only recognises one finger.
    /// </summary>
    Task<IReadOnlyList<DeviceTemplateRecord>> GetTemplatesAsync(
        string deviceUserId, CancellationToken ct = default);

    /// <summary>
    /// Attendance records held in device memory.
    ///
    /// Most models cannot filter by date and return the entire log every time.
    /// Treat <paramref name="since"/> as a hint, never a guarantee, and rely on
    /// the unique index in the database to reject what comes back twice.
    /// </summary>
    Task<IReadOnlyList<DevicePunchRecord>> GetPunchesAsync(
        DateTime? since = null, CancellationToken ct = default);

    // ── PUSH ────────────────────────────────────────────────────────────

    /// <summary>Create or update a user record on the device.</summary>
    Task<bool> UpsertUserAsync(DeviceUserRecord user, CancellationToken ct = default);

    /// <summary>
    /// Write one fingerprint template.
    ///
    /// The user record must already exist on the device - writing a template
    /// for an unknown enrol number is silently discarded by most firmware,
    /// which is the single most common cause of "I pushed the employee but the
    /// branch machine does not recognise them".
    /// </summary>
    Task<bool> WriteTemplateAsync(DeviceTemplateRecord template, CancellationToken ct = default);

    Task<bool> DeleteUserAsync(string deviceUserId, CancellationToken ct = default);

    /// <summary>
    /// Commit buffered changes to device flash. Some firmware keeps writes in
    /// RAM until this is called, and loses them on power cut.
    /// </summary>
    Task RefreshDataAsync(CancellationToken ct = default);

    /// <summary>Stop the device accepting punches while a bulk write is in progress.</summary>
    Task<bool> DisableAsync(int timeoutSeconds = 30, CancellationToken ct = default);
    Task<bool> EnableAsync(CancellationToken ct = default);
}

/// <summary>Creates a client per device. Sessions are short-lived and not shared.</summary>
public interface IZkDeviceClientFactory
{
    IZkDeviceClient Create();
}
