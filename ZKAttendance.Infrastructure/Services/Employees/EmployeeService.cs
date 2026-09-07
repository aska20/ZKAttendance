using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Domain.Entities;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Employees
{
	public class EmployeeService : IEmployeeService
	{
		private readonly EmployeeRepository _repository;
		private readonly ILogger<EmployeeService> _logger;
		private readonly AttendanceDbContext _context;

		public EmployeeService(
			EmployeeRepository repository,
			ILogger<EmployeeService> logger,
			AttendanceDbContext context)
		{
			_repository = repository;
			_logger = logger;
			_context = context;
		}

		public async Task<List<Employee>> GetAllEmployeesAsync()
		{
			try
			{
				return await _repository.GetAllAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error fetching the employee list");
				throw;
			}
		}

		public async Task<Employee?> GetEmployeeByIdAsync(int employeeId)
		{
			try
			{
				return await _repository.GetByIdAsync(employeeId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error fetching the employee {EmployeeId}", employeeId);
				throw;
			}
		}

		public async Task<Employee?> GetEmployeeByBiometricIdAsync(string biometricUserId)
		{
			try
			{
				return await _repository.GetByBiometricIdAsync(biometricUserId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error fetching employee by biometric ID: {BiometricId}", biometricUserId);
				throw;
			}
		}

		public async Task<List<Employee>> GetEmployeesByDepartmentIdAsync(int departmentId)
		{
			try
			{
				return await _repository.GetByDepartmentIdAsync(departmentId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error fetching department employees {DepartmentId}", departmentId);
				throw;
			}
		}

		// Next biometric ID = highest existing + 1, across employees and logs
		public async Task<string> GetNextBiometricUserIdAsync()
		{
			try
			{
				// 1. Highest number in Employees
				var employees = await _repository.GetAllAsync();
				var maxEmployeeId = employees
					.Select(e => e.BiometricUserId)
					.Where(id => int.TryParse(id, out _))
					.Select(id => int.Parse(id))
					.DefaultIfEmpty(0)
					.Max();

				// 2. Highest number in AttendanceLogs
				var attendanceLogs = await _context.AttendanceLogs
					.Select(a => a.BiometricUserId)
					.Distinct()
					.ToListAsync(); // materialise first

				var maxAttendanceLogId = attendanceLogs
					.Where(id => int.TryParse(id, out _)) // filter in memory
					.Select(id => int.Parse(id))
					.DefaultIfEmpty(0)
					.Max();

				// 3. Take whichever is larger
				var maxId = Math.Max(maxEmployeeId, maxAttendanceLogId);

				return (maxId + 1).ToString();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error getting the next biometric ID");
				return "1"; // fall back to 1 on error
			}
		}


		// Get biometric IDs that have no employee
		public async Task<List<string>> GetUnregisteredBiometricIdsAsync()
		{
			try
			{
				// Biometric IDs from Employees
				var employeeBiometricIds = await _repository.GetAllAsync()
					.ContinueWith(task => task.Result
						.Select(e => e.BiometricUserId)
						.ToList());

				// Biometric IDs from AttendanceLogs
				var attendanceBiometricIds = await _context.AttendanceLogs
					.Select(a => a.BiometricUserId)
					.Distinct()
					.ToListAsync();

				// IDs present in AttendanceLogs but missing from Employees
				var unregistered = attendanceBiometricIds
					.Except(employeeBiometricIds)
					.OrderBy(id => int.TryParse(id, out int num) ? num : int.MaxValue)
					.ToList();

				return unregistered;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error getting unregistered biometric IDs");
				return new List<string>();
			}
		}

		public async Task<Employee> CreateEmployeeAsync(Employee employee)
		{
			try
			{
				// Reject a duplicate BiometricUserId
				if (await _repository.BiometricIdExistsAsync(employee.BiometricUserId))
				{
					throw new InvalidOperationException(
						$"Employee No. '{employee.BiometricUserId}' already exists");
				}

				employee.IsActive = true;
				employee.CreatedDate = DateTime.Now;

				var result = await _repository.AddAsync(employee);
				_logger.LogInformation("Created employee: {EmployeeName}", employee.EmployeeName);

				if (!string.IsNullOrWhiteSpace(result.BiometricUserId))
				{
					var unmapped = await _context.AttendanceLogs
						.Where(a => a.BiometricUserId == result.BiometricUserId && a.EmployeeId == null)
						.ToListAsync();
					foreach (var p in unmapped)
						p.EmployeeId = result.EmployeeId;
					if (unmapped.Count > 0)
						await _context.SaveChangesAsync();
				}

				return result;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error creating employee: {EmployeeName}", employee.EmployeeName);
				throw;
			}
		}

		public async Task<Employee> UpdateEmployeeAsync(Employee employee)
		{
			try
			{
				if (!await _repository.ExistsAsync(employee.EmployeeId))
				{
					throw new InvalidOperationException("Employee not found");
				}

				// Reject a duplicate BiometricUserId
				if (await _repository.BiometricIdExistsAsync(employee.BiometricUserId, employee.EmployeeId))
				{
					throw new InvalidOperationException(
						$"Employee No. '{employee.BiometricUserId}' already exists");
				}

				employee.ModifiedDate = DateTime.Now;
				var result = await _repository.UpdateAsync(employee);

				_logger.LogInformation("Updated employee: {EmployeeName}", employee.EmployeeName);

				if (!string.IsNullOrWhiteSpace(result.BiometricUserId))
				{
					var unmapped = await _context.AttendanceLogs
						.Where(a => a.BiometricUserId == result.BiometricUserId && a.EmployeeId == null)
						.ToListAsync();
					foreach (var p in unmapped)
						p.EmployeeId = result.EmployeeId;
					if (unmapped.Count > 0)
						await _context.SaveChangesAsync();
				}

				return result;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error updating employee {EmployeeId}", employee.EmployeeId);
				throw;
			}
		}

		public async Task DeleteEmployeeAsync(int employeeId)
		{
			try
			{
				await _repository.SoftDeleteAsync(employeeId);
				_logger.LogInformation("Employee deleted {EmployeeId}", employeeId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error deleting employee {EmployeeId}", employeeId);
				throw;
			}
		}

		public async Task<bool> IsBiometricIdExistsAsync(string biometricUserId, int? excludeEmployeeId = null)
		{
			try
			{
				return await _repository.BiometricIdExistsAsync(biometricUserId, excludeEmployeeId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error checking biometric ID: {BiometricId}", biometricUserId);
				throw;
			}
		}

		public async Task<Dictionary<string, Employee>> GetEmployeesDictionaryAsync(List<string> biometricUserIds)
		{
			try
			{
				return await _repository.GetEmployeesDictionaryAsync(biometricUserIds);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error building the employee lookup");
				throw;
			}
		}

		public async Task<int> GetActiveEmployeeCountAsync()
		{
			try
			{
				return await _repository.GetActiveEmployeeCountAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error counting active employees");
				throw;
			}
		}
		// Get the most recent X biometric IDs
		public async Task<List<string>> GetLastBiometricUserIdsAsync(int count = 10)
		{
			try
			{
				// Collect IDs from employees and attendance logs
				var employeeIds = await _repository.GetAllAsync()
					.ContinueWith(task => task.Result
						.Select(e => e.BiometricUserId)
						.Where(id => int.TryParse(id, out _))
						.Select(id => int.Parse(id))
						.ToList());

				var attendanceIds = await _context.AttendanceLogs
					.Select(a => a.BiometricUserId)
					.Distinct()
					.ToListAsync();

				var attendanceIdsInt = attendanceIds
					.Where(id => int.TryParse(id, out _))
					.Select(id => int.Parse(id))
					.ToList();

				// Merge, de-duplicate and sort descending
				var allIds = employeeIds
					.Union(attendanceIdsInt)
					.OrderByDescending(id => id)
					.Take(count)
					.Select(id => id.ToString())
					.ToList();

				return allIds;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error getting the most recent biometric IDs");
				return new List<string>();
			}
		}

	}
}
