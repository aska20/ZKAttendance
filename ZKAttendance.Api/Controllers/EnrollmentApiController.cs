using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Api.Security;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>
    /// Biometric enrolment: getting a person who exists in this application to
    /// also exist on the terminals at the door.
    ///
    /// THE FLOW THE UI DRIVES
    /// ----------------------
    ///   POST .../push-user      create the user record on every terminal
    ///   POST .../start          the terminal switches to "place finger" /
    ///                           opens the face-registration camera
    ///   GET  .../status         poll: has a template appeared yet?
    ///   POST .../propagate      cache the template and copy it to the others
    ///
    /// Enrolment itself is a physical act — somebody has to stand at the
    /// machine. What this controller removes is the second physical act: doing
    /// it again at every other machine.
    /// </summary>
    [Route("api/Employees/{employeeId:int}/enrollment")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Enrollment")]
    [Authorize(Roles = Roles.Management)]
    public class EnrollmentApiController : ControllerBase
    {
        private readonly IDeviceEnrollmentOrchestrator _orchestrator;
        private readonly ILogger<EnrollmentApiController> _logger;

        public EnrollmentApiController(
            IDeviceEnrollmentOrchestrator orchestrator,
            ILogger<EnrollmentApiController> logger)
        {
            _orchestrator = orchestrator;
            _logger = logger;
        }

        /// <summary>
        /// Where this employee stands on every active terminal: is the user
        /// record there, and how many fingers are enrolled.
        /// </summary>
        [HttpGet("status")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Status(int employeeId, CancellationToken ct)
        {
            try
            {
                var devices = await _orchestrator.GetStatusAsync(employeeId, ct);
                return Ok(new
                {
                    employeeId,
                    deviceCount = devices.Count,
                    enrolledEverywhere = devices.Count > 0 && devices.All(d => d.TemplateCount > 0),
                    devices
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>
        /// Create or refresh the user record on the terminals, without enrolling
        /// a finger. Their name then shows on the terminal display, and any
        /// punch under that enrol number attributes correctly.
        /// </summary>
        [HttpPost("push-user")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> PushUser(
            int employeeId, [FromQuery] int? deviceId, CancellationToken ct)
        {
            try
            {
                var report = await _orchestrator.PushUserToDevicesAsync(employeeId, deviceId, ct);
                return Ok(report);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>
        /// Put a terminal into registration mode for this employee. Defaults to
        /// the master device.
        /// </summary>
        /// <param name="employeeId">The employee being enrolled.</param>
        /// <param name="deviceId">Which terminal. Omit for the master.</param>
        /// <param name="fingerIndex">0-9. Ignored by face/palm units.</param>
        /// <param name="ct">Request cancellation token.</param>
        [HttpPost("start")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Start(
            int employeeId,
            [FromQuery] int? deviceId,
            [FromQuery] int fingerIndex = 0,
            CancellationToken ct = default)
        {
            try
            {
                var result = await _orchestrator.StartEnrollmentAsync(employeeId, deviceId, fingerIndex, ct);
                _logger.LogInformation(
                    "Enrolment start for employee {Id} on {Device} by {User}: {Started}",
                    employeeId, result.DeviceName, User.Identity?.Name, result.Started);

                // A terminal that will not start is a real failure the operator
                // needs to see, not a 200 with a quiet flag.
                return result.Started ? Ok(result) : StatusCode(409, result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Stop a terminal that is waiting on a scan.</summary>
        [HttpPost("cancel")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Cancel(
            int employeeId, [FromQuery] int? deviceId, CancellationToken ct)
        {
            var ok = await _orchestrator.CancelEnrollmentAsync(employeeId, deviceId, ct);
            return Ok(new { employeeId, cancelled = ok });
        }

        /// <summary>
        /// Pull whatever the master now holds for this employee, cache it, and
        /// write it to every other active terminal.
        /// </summary>
        [HttpPost("propagate")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Propagate(int employeeId, CancellationToken ct)
        {
            try
            {
                var report = await _orchestrator.PullAndPropagateAsync(employeeId, ct);
                _logger.LogInformation(
                    "Propagated employee {Id}: {Cached} template(s), {Updated} device(s) updated, {Failed} failed",
                    employeeId, report.TemplatesCached, report.DevicesUpdated, report.DevicesFailed);
                return Ok(report);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }
    }

    /// <summary>Device-wide enrolment operations, as opposed to per-employee ones.</summary>
    [Route("api/Devices/{deviceId:int}")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Enrollment")]
    [Authorize(Roles = Roles.Management)]
    public class DeviceProvisioningApiController : ControllerBase
    {
        private readonly IDeviceEnrollmentOrchestrator _orchestrator;

        public DeviceProvisioningApiController(IDeviceEnrollmentOrchestrator orchestrator)
            => _orchestrator = orchestrator;

        /// <summary>
        /// Fill a newly registered terminal from the templates already cached in
        /// the database. This is what makes adding a third or fourth machine a
        /// background job rather than calling every employee back to the sensor.
        /// </summary>
        [HttpPost("provision")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> Provision(int deviceId, CancellationToken ct)
        {
            try
            {
                var written = await _orchestrator.ProvisionDeviceAsync(deviceId, ct);
                return Ok(new
                {
                    deviceId,
                    employeesWritten = written,
                    message = written == 0
                        ? "Nothing was written. Either no employee has a cached fingerprint yet, or the terminal is unreachable."
                        : $"{written} employee(s) written to the terminal."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.From(ex.Message));
            }
        }

        /// <summary>Re-check every terminal and repair any that has fallen behind.</summary>
        [HttpPost("reconcile-all")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> ReconcileAll(CancellationToken ct)
        {
            var repaired = await _orchestrator.ReconcileAllAsync(ct);
            return Ok(new { repaired });
        }
    }
}
