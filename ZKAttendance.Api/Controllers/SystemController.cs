using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Services.Devices;
using ZKAttendance.Infrastructure.NepaliCalendar;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Read-only view of the system, plus a manual sync trigger.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// The MVC pages return HTML. There was no way to inspect what the
    /// background jobs are actually doing without opening SQL Server. These
    /// endpoints expose the same data as JSON, which makes the system testable
    /// from Swagger and demonstrable without clicking through screens.
    ///
    /// Everything here is a thin wrapper over the existing services. No
    /// business logic lives in this file.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    public class SystemController : ControllerBase
    {
        private readonly IDeviceService _devices;
        private readonly IEmployeeService _employees;
        private readonly IBrancheService _branches;
        private readonly IAttendanceSyncService _sync;
        private readonly IDeviceMonitorService _monitor;
        private readonly INepaliDateService _nepali;
        private readonly ILogger<SystemController> _logger;

        public SystemController(
            IDeviceService devices,
            IEmployeeService employees,
            IBrancheService branches,
            IAttendanceSyncService sync,
            IDeviceMonitorService monitor,
            INepaliDateService nepali,
            ILogger<SystemController> logger)
        {
            _devices = devices;
            _employees = employees;
            _branches = branches;
            _sync = sync;
            _monitor = monitor;
            _nepali = nepali;
            _logger = logger;
        }

        /// <summary>
        /// Overall health: how many devices, how many are online, employee count,
        /// and today's date in both calendars.
        /// </summary>
        /// <remarks>Start here. If this returns, the database connection works.</remarks>
        [HttpGet("status")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetStatus()
        {
            var devices = await _devices.GetAllDevicesAsync();
            var online = await _devices.GetOnlineDevicesAsync();
            var employeeCount = await _employees.GetActiveEmployeeCountAsync();
            var branches = await _branches.GetAllBranchesAsync();

            return Ok(new
            {
                serverTime = DateTime.Now,
                todayAd = DateTime.Today.ToString("yyyy-MM-dd"),
                todayBs = _nepali.Format(DateTime.Today),
                counts = new
                {
                    branches = branches.Count,
                    devices = devices.Count,
                    devicesOnline = online.Count,
                    devicesOffline = devices.Count - online.Count,
                    activeEmployees = employeeCount
                }
            });
        }

        /// <summary>Every registered device with its connection state.</summary>
        [HttpGet("devices")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetDevices()
        {
            var devices = await _devices.GetAllDevicesAsync();

            return Ok(devices.Select(d => new
            {
                d.DeviceId,
                d.DeviceName,
                d.DeviceIP,
                d.DevicePort,
                d.SerialNumber,
                d.BranchId,
                role = d.Role.ToString(),
                d.IsActive,
                d.IsOnline,
                d.ConnectionStatus,
                d.LastCheckTime,
                d.LastConnectionTime
            }));
        }

        /// <summary>Ping and port-test one device right now, without waiting for the timer.</summary>
        [HttpGet("devices/{deviceId:int}/test")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> TestDevice(int deviceId)
        {
            var device = await _devices.GetDeviceByIdAsync(deviceId);
            if (device is null)
                return NotFound(new { message = $"Device {deviceId} not found" });

            var (success, message) = await _devices.TestDeviceConnectionAsync(deviceId);
            return Ok(new { deviceId, device.DeviceName, device.DeviceIP, success, message });
        }

        /// <summary>
        /// Run a sync round immediately against every active device.
        /// </summary>
        /// <remarks>
        /// Normally this happens on a five-minute timer. This endpoint is for
        /// testing: it returns exactly what the background job would have
        /// logged, so you can see fetched / inserted / duplicate / unmapped
        /// counts per device without watching the console.
        /// </remarks>
        [HttpPost("sync")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> RunSync(CancellationToken ct)
        {
            _logger.LogInformation("Manual sync triggered through the API");

            var results = await _sync.SyncAllDevicesAsync(ct);

            return Ok(new
            {
                devicesProcessed = results.Count,
                succeeded = results.Count(r => r.Success),
                totalInserted = results.Sum(r => r.Inserted),
                totalDuplicates = results.Sum(r => r.Duplicates),
                totalUnmapped = results.Sum(r => r.Unmapped),
                perDevice = results
            });
        }

        /// <summary>Run a sync round against a single device.</summary>
        [HttpPost("sync/{deviceId:int}")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> RunSyncForDevice(int deviceId, CancellationToken ct)
            => Ok(await _sync.SyncDeviceAsync(deviceId, ct));

        /// <summary>Force an immediate online/offline check of every device.</summary>
        [HttpPost("devices/check")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> CheckDevices()
        {
            await _monitor.CheckDevicesStatusAsync();
            var online = await _devices.GetOnlineDevicesAsync();
            var all = await _devices.GetAllDevicesAsync();
            return Ok(new { checkedCount = all.Count, onlineCount = online.Count });
        }

        /// <summary>All active employees.</summary>
        [HttpGet("employees")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetEmployees()
        {
            var employees = await _employees.GetAllEmployeesAsync();

            return Ok(employees.Select(e => new
            {
                e.EmployeeId,
                e.EmployeeName,
                e.BiometricUserId,
                e.DepartmentId,
                e.IsActive,
                hireDateAd = e.HireDate,
                hireDateBs = e.HireDate.HasValue ? _nepali.Format(e.HireDate.Value) : null
            }));
        }

        /// <summary>
        /// Biometric IDs that appear in attendance logs but belong to no employee.
        /// </summary>
        /// <remarks>
        /// These are punches the system stored but could not attribute. They are
        /// kept rather than discarded, because losing a real punch over missing
        /// paperwork is worse than holding an unattributed row.
        /// </remarks>
        [HttpGet("employees/unregistered")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetUnregistered()
            => Ok(await _employees.GetUnregisteredBiometricIdsAsync());

        /// <summary>All branches with their devices.</summary>
        [HttpGet("branches")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetBranches()
        {
            var branches = await _branches.GetAllBranchesWithDevicesAsync();

            return Ok(branches.Select(b => new
            {
                b.BranchId,
                b.BranchCode,
                b.BranchName,
                b.City,
                b.IsActive,
                devices = b.Devices?.Select(d => new { d.DeviceId, d.DeviceName, d.DeviceIP, d.IsOnline })
            }));
        }

        /// <summary>
        /// Convert a Gregorian date to Bikram Sambat.
        /// </summary>
        /// <param name="date">Gregorian date, yyyy-MM-dd. Defaults to today.</param>
        /// <remarks>
        /// Supported range is 2080-2086 BS. Outside it the converter throws,
        /// deliberately, rather than returning a wrong date.
        /// </remarks>
        [HttpGet("nepali-date")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public IActionResult ConvertDate([FromQuery] DateTime? date = null)
        {
            var ad = date ?? DateTime.Today;
            try
            {
                var bs = _nepali.ToBs(ad);
                return Ok(new
                {
                    gregorian = ad.ToString("yyyy-MM-dd"),
                    bikramSambat = bs.ToString(),
                    longForm = bs.ToLongString(),
                    devanagari = bs.ToNepaliString(),
                    isWeeklyOff = _nepali.IsWeeklyOff(ad)
                });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>First and last Gregorian date of a Bikram Sambat month.</summary>
        /// <remarks>Used by monthly reports, which must run Baisakh 1 to the
        /// end of Baisakh rather than the 1st to the 31st of an AD month.</remarks>
        [HttpGet("nepali-date/month-range")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public IActionResult MonthRange([FromQuery] int bsYear, [FromQuery] int bsMonth)
        {
            try
            {
                var (from, to) = _nepali.MonthRange(bsYear, bsMonth);
                return Ok(new { bsYear, bsMonth, fromAd = from.ToString("yyyy-MM-dd"), toAd = to.ToString("yyyy-MM-dd") });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
