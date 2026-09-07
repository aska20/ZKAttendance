using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZKAttendance.Domain.Enums;

namespace ZKAttendance.Application.Abstractions
{
    /// <summary>One punch exactly as the device reports it.</summary>
    public record DevicePunch(
        string BiometricUserId,
        DateTime PunchTime,
        int VerifyMode,      // 0 password, 1 fingerprint, 2 card
        int InOutMode,       // 0 in, 1 out, 2 break-out, 3 break-in, 4/5 OT in/out
        int WorkCode);

    /// <summary>One user record stored on the device.</summary>
    public record DeviceUser(string BiometricUserId, string Name, int Privilege, bool Enabled)
    {
        /// <summary>
        /// The device's internal slot number. Distinct from BiometricUserId:
        /// the enrol number is what the operator types and what appears in
        /// punches, the Uid is the row the firmware stores it in. Template
        /// reads and writes address the Uid, not the enrol number.
        /// </summary>
        public int Uid { get; init; }

        /// <summary>How many fingers this user has enrolled, when known.</summary>
        public int FingerCount { get; init; }
    }

    /// <summary>
    /// One enrolled finger. <paramref name="FormatVersion"/> is 9 or 10 —
    /// ZKTeco firmware families use incompatible template encodings, and a v10
    /// template written to a v9 terminal will never match. Carrying the version
    /// lets propagation refuse loudly instead of failing silently at the door.
    /// </summary>
    public record FingerTemplate(
        string BiometricUserId,
        int FingerIndex,
        byte[] Data,
        int FormatVersion = 10)
    {
        public int Uid { get; init; }
        public bool Valid { get; init; } = true;
    }

    public record DeviceInfo(
        string SerialNumber,
        string FirmwareVersion,
        int UserCount,
        int LogCount,
        DateTime DeviceTime);

    /// <summary>Outcome of asking a terminal to start capturing a finger.</summary>
    public record EnrollResult(bool Started, string Message, int FingerIndex);

    /// <summary>
    /// Everything the application needs from a physical ZKTeco machine.
    ///
    /// WHY AN INTERFACE
    /// ----------------
    /// The vendor SDK (zkemkeeper.dll) is a 32-bit COM component. It only runs
    /// on Windows, must be registered with regsvr32, and cannot be used from a
    /// unit test or a CI build. Putting it behind this interface means the sync
    /// and enrolment services can be exercised with a fake reader, and the rest
    /// of the codebase never references a COM type.
    ///
    /// READ vs WRITE
    /// -------------
    /// The original interface was read-only, which is why a person added in
    /// this application never appeared on the terminal. The write half below is
    /// what closes that gap: create the user, ask the terminal to capture a
    /// finger, pull the resulting template back, and push it to every other
    /// terminal so all devices agree on who exists.
    /// </summary>
    public interface IZkDeviceReader : IDisposable
    {
        /// <param name="commPassword">The device Comm Key. 0 = none.</param>
        Task<bool> ConnectAsync(string ip, int port, int commPassword = 0);
        Task DisconnectAsync();

        Task<DeviceInfo?> GetDeviceInfoAsync();

        /// <summary>Push server time to the device so branch clocks stay aligned.</summary>
        Task<bool> SetDeviceTimeAsync(DateTime serverTime);

        /// <summary>All users enrolled on this device.</summary>
        Task<List<DeviceUser>> GetUsersAsync();

        /// <summary>
        /// All attendance records currently in device memory.
        ///
        /// Note: most ZKTeco models cannot filter by date — they hand you the
        /// whole log. Filtering by <paramref name="since"/> happens in the
        /// wrapper, and the unique index in SQL Server catches whatever slips
        /// through. Do not rely on the device to give you only new records.
        /// </summary>
        Task<List<DevicePunch>> GetAttendanceLogsAsync(DateTime? since = null);

        // ── write half ───────────────────────────────────────────────────

        /// <summary>
        /// Create or overwrite the user record on the terminal. This is the
        /// step that makes the name show on the display and lets the enrol
        /// number be captured against; it does NOT enrol a finger.
        /// </summary>
        Task<bool> SetUserAsync(
            string biometricUserId,
            string name,
            int privilege = 0,
            string password = "",
            bool enabled = true);

        /// <summary>Remove the user and their templates from this terminal.</summary>
        Task<bool> DeleteUserAsync(string biometricUserId);

        /// <summary>
        /// Put the terminal into enrolment mode for this user, so the screen
        /// switches to "place your finger" / "look at the camera" and the
        /// person standing there can register on the spot.
        ///
        /// The call returns as soon as the terminal accepts the command — it
        /// does not block for the person to finish. Poll GetTemplatesAsync (or
        /// let the propagation job do it) to find out whether they did.
        /// </summary>
        Task<EnrollResult> StartRemoteEnrollAsync(string biometricUserId, int fingerIndex = 0);

        /// <summary>Abort an enrolment or verification the terminal is waiting on.</summary>
        Task<bool> CancelCaptureAsync();

        /// <summary>
        /// Read enrolled templates back off the terminal, for one user or for
        /// everybody. This is what lets a new terminal be provisioned without
        /// calling every employee back to press their finger again.
        /// </summary>
        Task<List<FingerTemplate>> GetTemplatesAsync(string? biometricUserId = null);

        /// <summary>Write one cached template onto this terminal.</summary>
        Task<bool> SetTemplateAsync(FingerTemplate template);

        /// <summary>
        /// Ask the terminal to reload its user table from flash. Required after
        /// user or template writes on most firmware, otherwise the change is
        /// only visible after a reboot.
        /// </summary>
        Task<bool> RefreshDataAsync();
    }
}
