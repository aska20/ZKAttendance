using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Services.Common;

namespace ZKAttendance.Web.Controllers
{
    [Authorize(Roles = "Admin,HR")]
    public class DevicesController : Controller
    {
        private readonly IDeviceService _deviceService;
        private readonly LookupService _lookupService;
        private readonly ILogger<DevicesController> _logger;

        public DevicesController(
            IDeviceService deviceService,
            LookupService lookupService,
            ILogger<DevicesController> logger)
        {
            _deviceService = deviceService;
            _lookupService = lookupService;
            _logger = logger;
        }

        // GET: Devices
        public async Task<IActionResult> Index()
        {
            try
            {
                var devices = await _deviceService.GetAllDevicesAsync();
                return View(devices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing devices");
                TempData["Error"] = "An error occurred while loading the device list";
                return View(new List<Device>());
            }
        }

        // GET: Devices/Create
        public async Task<IActionResult> Create()
        {
            await PopulateBranches();
            return View();
        }

        // POST: Devices/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Device device)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _deviceService.CreateDeviceAsync(device);
                    TempData["Success"] = "Device created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating device");
                    ModelState.AddModelError("", "An error occurred while creating the device");
                }
            }

            await PopulateBranches(device.BranchId);
            return View(device);
        }

        // GET: Devices/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var device = await _deviceService.GetDeviceByIdAsync(id);
                if (device == null)
                {
                    TempData["Error"] = "Device not found";
                    return RedirectToAction(nameof(Index));
                }

                await PopulateBranches(device.BranchId);
                return View(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading device data {DeviceId}", id);
                TempData["Error"] = "An error occurred while loading device data";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Devices/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Device device)
        {
            if (id != device.DeviceId)
            {
                TempData["Error"] = "Invalid data";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _deviceService.UpdateDeviceAsync(device);
                    TempData["Success"] = "Device updated successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating device {DeviceId}", id);
                    ModelState.AddModelError("", "An error occurred while updating the device");
                }
            }

            await PopulateBranches(device.BranchId);
            return View(device);
        }

        // GET: Devices/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var device = await _deviceService.GetDeviceByIdAsync(id);
                if (device == null)
                {
                    TempData["Error"] = "Device not found";
                    return RedirectToAction(nameof(Index));
                }
                return View(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing device details {DeviceId}", id);
                TempData["Error"] = "An error occurred while showing device details";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Devices/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _deviceService.DeleteDeviceAsync(id);
                TempData["Success"] = "Device deleted successfully";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting device {DeviceId}", id);
                TempData["Error"] = "An error occurred while deleting the device";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Devices/TestConnection/5
        [HttpPost]
        public async Task<IActionResult> TestConnection(int id)
        {
            try
            {
                var result = await _deviceService.TestDeviceConnectionAsync(id);

                if (result.Success)
                {
                    return Json(new { success = true, message = result.Message });
                }
                else
                {
                    return Json(new { success = false, message = result.Message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing the device connection {DeviceId}", id);
                return Json(new { success = false, message = "An error occurred while testing the connection" });
            }
        }

        // ✅ NEW: API endpoint for real-time status updates
        [HttpGet]
        public async Task<IActionResult> GetDevicesStatus()
        {
            try
            {
                var devices = await _deviceService.GetAllDevicesAsync();

                var statusList = devices.Select(d => new
                {
                    deviceId = d.DeviceId,
                    isOnline = d.IsOnline,
                    lastConnectionTime = d.LastConnectionTime?.ToString("yyyy/MM/dd hh:mm tt"),
                    connectionStatus = d.ConnectionStatus,
                    isActive = d.IsActive
                });

                var stats = new
                {
                    total = devices.Count,
                    online = devices.Count(d => d.IsOnline && d.IsActive),
                    offline = devices.Count(d => !d.IsOnline && d.IsActive),
                    inactive = devices.Count(d => !d.IsActive)
                };

                return Json(new { success = true, devices = statusList, stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching device status");
                return Json(new { success = false, message = "An error occurred" });
            }
        }

        // Helper method
        private async Task PopulateBranches(int? selectedBranchId = null)
        {
            var branches = await _lookupService.GetActiveBranchesAsync();
            ViewBag.Branches = new SelectList(branches, "BranchId", "BranchName", selectedBranchId);
        }
    }
}
