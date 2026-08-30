using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Application.Dtos.Reports;
using ZKAttendance.Application.Services.Attendances;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos;
using ZKAttendance.Infrastructure.Services.Common;
using ZKAttendance.Infrastructure.Services.Report;

namespace ZKAttendance.Web.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly IReportService _reportService;
        private readonly IAttendanceService _attendanceService;  
        private readonly LookupService _lookupService;
        private readonly ILogger<ReportsController> _logger;     

        public ReportsController(
            IReportService reportService,
            IAttendanceService attendanceService,  
            LookupService lookupService,
            ILogger<ReportsController> logger)     
        {
            _reportService = reportService;
            _attendanceService = attendanceService;
            _lookupService = lookupService;
            _logger = logger;
        }

        // GET: Reports/Index
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Daily));
        }

        // ═════════════════════════════════════════════════════════════
        // Daily - primary daily attendance report
        // ═════════════════════════════════════════════════════════════

        // GET: Reports/Daily
        public async Task<IActionResult> Daily(DateTime? date, int? branchId, string? department)
        {
            try
            {
                var selectedDate = date ?? DateTime.Today;
                var report = await _reportService.GetDailyAttendanceReportAsync(selectedDate, branchId, department);

                ViewBag.SelectedDate = selectedDate;
                ViewBag.SelectedBranchId = branchId;
                ViewBag.SelectedDepartment = department;
                ViewBag.Branches = await _lookupService.GetActiveBranchesAsync();
                ViewBag.Departments = await _lookupService.GetActiveDepartmentsAsync();

                return View(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating daily report for date {Date}", date);
                TempData["ErrorMessage"] = "An error occurred while generating the daily report.";
                return View(new DailyAttendanceReportDto { Date = date ?? DateTime.Today });
            }
        }

        // ═════════════════════════════════════════════════════════════
        // DailyReportFilter - daily report with advanced filters
        // ═════════════════════════════════════════════════════════════

        // GET: Reports/DailyReportFilter
        public async Task<IActionResult> DailyReportFilter()
        {
            try
            {
                var model = new DailyReportFilterViewModel
                {
                    Branches = await _lookupService.GetActiveBranchesAsync(),
                    Devices = await _lookupService.GetActiveDevicesAsync(),
                    Employees = await _lookupService.GetActiveEmployeesAsync(),
                    DateFrom = DateTime.Today.AddDays(-7),
                    DateTo = DateTime.Today
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading the filter page");
                TempData["Error"] = "An error occurred while loading the page";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Reports/DailyReportFilter
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DailyReportFilter(DailyReportFilterViewModel filter)
        {
            if (!ModelState.IsValid)
            {
                await PopulateFilterLists(filter);
                return View(filter);
            }

            try
            {
                var report = await _reportService.GetDailyAttendanceRangeReportAsync(
                    filter.DateFrom,
                    filter.DateTo,
                    filter.SelectedBranchId,
                    filter.SelectedDeviceId,
                    filter.SelectedEmployeeId);

                filter.Report = report;
                await PopulateFilterLists(filter);

                return View(filter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating the report");
                ModelState.AddModelError("", $"An error occurred while generating the report: {ex.Message}");
                await PopulateFilterLists(filter);
                return View(filter);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // DailyAttendance - full daily attendance report
        // ═════════════════════════════════════════════════════════════

        // GET: Reports/DailyAttendance
        public async Task<IActionResult> DailyAttendance(
            DateTime? date,
            int? branchId,
            int? departmentId)
        {
            try
            {
                var selectedDate = date ?? DateTime.Today;

                // Load the full report including present and absent lists
                var summary = await _attendanceService.GetDailyAttendanceReportSummaryAsync(
                    selectedDate,
                    branchId,
                    departmentId);

                // Fill ViewBag for the filters
                ViewBag.SelectedDate = selectedDate;
                ViewBag.BranchId = branchId;
                ViewBag.DepartmentId = departmentId;
                ViewBag.Branches = await _lookupService.GetActiveBranchesAsync();
                ViewBag.Departments = await _lookupService.GetActiveDepartmentsAsync();

                return View(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing the daily attendance report");
                TempData["Error"] = "An error occurred while loading the report";
                return View(new DailyAttendanceReportSummaryDto());
            }
        }

        // ═════════════════════════════════════════════════════════════
        // MonthlyAttendance - monthly report
        // ═════════════════════════════════════════════════════════════

        // GET: Reports/MonthlyAttendance
        public async Task<IActionResult> MonthlyAttendance(
            int? year,
            int? month,
            int? branchId)
        {
            try
            {
                var selectedYear = year ?? DateTime.Today.Year;
                var selectedMonth = month ?? DateTime.Today.Month;

                ViewBag.SelectedYear = selectedYear;
                ViewBag.SelectedMonth = selectedMonth;
                ViewBag.BranchId = branchId;
                ViewBag.Branches = await _lookupService.GetActiveBranchesAsync();

                // TODO: add a monthly report service
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing the monthly attendance report");
                TempData["Error"] = "An error occurred while loading the report";
                return View();
            }
        }

        // ═════════════════════════════════════════════════════════════
        // EmployeeAttendance - single employee report
        // ═════════════════════════════════════════════════════════════

        // GET: Reports/EmployeeAttendance
        public async Task<IActionResult> EmployeeAttendance(
            int? employeeId,
            DateTime? fromDate,
            DateTime? toDate)
        {
            try
            {
                ViewBag.EmployeeId = employeeId;
                ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
                ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
                ViewBag.Employees = await _lookupService.GetActiveEmployeesAsync();

                // TODO: add an employee report service
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing the employee attendance report");
                TempData["Error"] = "An error occurred while loading the report";
                return View();
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Export actions
        // ═════════════════════════════════════════════════════════════

        // GET: Reports/ExportDailyToExcel
        public async Task<IActionResult> ExportDailyToExcel(
            DateTime? date,
            int? branchId,
            int? departmentId)
        {
            try
            {
                var selectedDate = date ?? DateTime.Today;
                var summary = await _attendanceService.GetDailyAttendanceReportSummaryAsync(
                    selectedDate,
                    branchId,
                    departmentId);

                // TODO: use ExcelReportService
                // var excelFile = await _reportService.ExportDailyAttendanceToExcelAsync(summary);
                // return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
                //             $"DailyAttendance_{selectedDate:yyyy-MM-dd}.xlsx");

                TempData["Info"] = "Export is not implemented yet";
                return RedirectToAction(nameof(DailyAttendance), new { date, branchId, departmentId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting the report to Excel");
                TempData["Error"] = "An error occurred while exporting";
                return RedirectToAction(nameof(DailyAttendance));
            }
        }

        // GET: Reports/ExportDailyToPdf
        public async Task<IActionResult> ExportDailyToPdf(
            DateTime? date,
            int? branchId,
            int? departmentId)
        {
            try
            {
                var selectedDate = date ?? DateTime.Today;
                var summary = await _attendanceService.GetDailyAttendanceReportSummaryAsync(
                    selectedDate,
                    branchId,
                    departmentId);

                // TODO: use PdfReportService
                // var pdfFile = await _reportService.ExportDailyAttendanceToPdfAsync(summary);
                // return File(pdfFile, "application/pdf", 
                //             $"DailyAttendance_{selectedDate:yyyy-MM-dd}.pdf");

                TempData["Info"] = "Export is not implemented yet";
                return RedirectToAction(nameof(DailyAttendance), new { date, branchId, departmentId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting the report to PDF");
                TempData["Error"] = "An error occurred while exporting";
                return RedirectToAction(nameof(DailyAttendance));
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Helper Methods
        // ═════════════════════════════════════════════════════════════

        private async Task PopulateFilterLists(DailyReportFilterViewModel filter)
        {
            filter.Branches = await _lookupService.GetActiveBranchesAsync();
            filter.Devices = await _lookupService.GetActiveDevicesAsync();
            filter.Employees = await _lookupService.GetActiveEmployeesAsync();
        }
    }
}
