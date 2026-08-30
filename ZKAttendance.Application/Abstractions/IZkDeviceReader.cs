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
    public record DeviceUser(string BiometricUserId, string Name, int Privilege, bool Enabled);

    public record DeviceInfo(
        string SerialNumber,
        string FirmwareVersion,
        int UserCount,
        int LogCount,
        DateTime DeviceTime);

    /// <summary>
    /// Everything the application needs from a physical ZKTeco machine.
    ///
    /// WHY AN INTERFACE
    /// ----------------
    /// The real ZKTeco SDK (zkemkeeper.dll) is a 32-bit COM component. It only
    /// runs on Windows, it must be registered with regsvr32, and it cannot be
    /// used from a unit test or from a CI build. Putting it behind this
    /// interface means AttendanceSyncService can be tested with a fake reader,
    /// and the rest of the codebase never references the COM type.
    ///
    /// Two implementations:
    ///   ZkemkeeperDeviceReader - the real one, wraps zkemkeeper.CZKEM
    ///   FakeDeviceReader       - returns canned punches, for testing and demos
    /// </summary>
    public interface IZkDeviceReader : IDisposable
    {
        Task<bool> ConnectAsync(string ip, int port);
        Task DisconnectAsync();

        Task<DeviceInfo?> GetDeviceInfoAsync();

        /// <summary>Push server time to the device so branch clocks stay aligned.</summary>
        Task<bool> SetDeviceTimeAsync(DateTime serverTime);

        /// <summary>All users enrolled on this device.</summary>
        Task<List<DeviceUser>> GetUsersAsync();

        /// <summary>
        /// All attendance records currently in device memory.
        ///
        /// Note: most ZKTeco models cannot filter by date - they hand you the
        /// whole log. Filtering by <paramref name="since"/> happens in the
        /// wrapper, and the unique index in SQL Server catches whatever slips
        /// through. Do not rely on the device to give you only new records.
        /// </summary>
        Task<List<DevicePunch>> GetAttendanceLogsAsync(DateTime? since = null);
    }
}
