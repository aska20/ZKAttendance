#nullable disable
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Configuration;
using ZKAttendance.Domain.Entities;                

namespace ZKAttendance.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfigurationController : ControllerBase
    {
        private readonly IBrancheService _branchService;
        private readonly IDeviceService _deviceService;
        private readonly ILogger<ConfigurationController> _logger;

        public ConfigurationController(
            IBrancheService branchService,
            IDeviceService deviceService,
            ILogger<ConfigurationController> logger)
        {
            _branchService = branchService;
            _deviceService = deviceService;
            _logger = logger;
        }

        /// <summary>
        /// Get one branch together with its devices
        /// </summary>
        [HttpGet("Branch/{branchCode}")]
        [ProducesResponseType(typeof(BranchConfigurationDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetBranchConfiguration(string branchCode)
        {
            try
            {
                _logger.LogInformation("Branch configuration requested: {BranchCode}", branchCode);

                // Use the dedicated method rather than GetAll
                var branch = await _branchService.GetBranchByCodeAsync(branchCode);

                if (branch == null)
                {
                    _logger.LogWarning("Branch not found: {BranchCode}", branchCode);
                    return NotFound(new { message = $"Branch {branchCode} not found" });
                }

                var devices = await _deviceService.GetDevicesByBranchIdAsync(branch.BranchId);

                var response = new BranchConfigurationDto
                {
                    Branch = new BranchDto
                    {
                        BranchId = branch.BranchId,
                        BranchCode = branch.BranchCode,
                        BranchName = branch.BranchName,
                        City = branch.City
                    },
                    Devices = devices.Select(d => new DeviceDto
                    {
                        DeviceId = d.DeviceId,
                        DeviceName = d.DeviceName,
                        DeviceIP = d.DeviceIP,
                        DevicePort = d.DevicePort,
                        SerialNumber = d.SerialNumber,
                        DeviceModel = d.DeviceModel,
                        IsActive = d.IsActive
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching branch information: {BranchCode}", branchCode);
                return StatusCode(500, new { message = "A server error occurred" });
            }
        }

        /// <summary>
        /// Get all branches together with their devices
        /// </summary>
        [HttpGet("AllBranches")]
        [ProducesResponseType(typeof(List<BranchConfigurationDto>), 200)]
        public async Task<IActionResult> GetAllBranchesConfiguration()
        {
            try
            {
                _logger.LogInformation("All branch and device configuration requested");

                // Load everything in one query using Include
                var branchesWithDevices = await _branchService.GetAllBranchesWithDevicesAsync();

                var response = branchesWithDevices.Select(branch => new BranchConfigurationDto
                {
                    Branch = new BranchDto
                    {
                        BranchId = branch.BranchId,
                        BranchCode = branch.BranchCode,
                        BranchName = branch.BranchName,
                        City = branch.City
                    },
                    Devices = branch.Devices.Select(d => new DeviceDto
                    {
                        DeviceId = d.DeviceId,
                        DeviceName = d.DeviceName,
                        DeviceIP = d.DeviceIP,
                        DevicePort = d.DevicePort,
                        SerialNumber = d.SerialNumber,
                        DeviceModel = d.DeviceModel,
                        IsActive = d.IsActive
                    }).ToList()
                }).ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all branch information");
                return StatusCode(500, new { message = "A server error occurred" });
            }
        }
    }
}
