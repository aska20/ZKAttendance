using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Infrastructure.Security;
using ZKAttendance.Web.Models;

namespace ZKAttendance.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly AttendanceDbContext _context;
        private readonly EmployeeAccountLinker _accountLinker;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            AttendanceDbContext context,
            EmployeeAccountLinker accountLinker,
            ILogger<AccountController> logger)
        {
            _context = context;
            _accountLinker = accountLinker;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════
        // LOGIN
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToLocal(returnUrl, User.FindFirstValue(ClaimTypes.Role));
            }

            var model = new LoginViewModel
            {
                ReturnUrl = returnUrl
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            model.ReturnUrl = returnUrl ?? model.ReturnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var identifier = model.Username.Trim();
            var user = await _context.ApiUsers
                .FirstOrDefaultAsync(u => u.Username.ToLower() == identifier.ToLower() || u.Email.ToLower() == identifier.ToLower());

            if (user == null || !user.IsActive || !PasswordHasher.VerifyPassword(model.Password, user.PasswordHash, user.PasswordSalt))
            {
                _logger.LogWarning("Failed login attempt for identifier: {Identifier}", identifier);
                ModelState.AddModelError(string.Empty, "Invalid username/email or password.");
                return View(model);
            }

            // Create Claims
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.ApiUserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8),
                IssuedUtc = DateTimeOffset.UtcNow
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            user.LastLoginDate = DateTime.Now;
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {Username} logged in successfully via Web portal.", user.Username);
            TempData["SuccessMessage"] = $"Welcome back, {user.Username}!";

            return RedirectToLocal(model.ReturnUrl, user.Role);
        }

        // ═══════════════════════════════════════════════════════
        // REGISTER
        // ═══════════════════════════════════════════════════════

        // Account creation is an administrator task, not self-service. The
        // seeded 'admin' account is the bootstrap; everyone else is created here.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var username = model.Username.Trim();
            var email = model.Email.Trim();

            if (await _context.ApiUsers.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            {
                ModelState.AddModelError("Username", "This username is already taken.");
                return View(model);
            }

            if (await _context.ApiUsers.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            {
                ModelState.AddModelError("Email", "This email address is already registered.");
                return View(model);
            }

            var allowedRoles = new[] { "Admin", "HR", "Employee" };
            var role = allowedRoles.FirstOrDefault(r => r.Equals(model.Role, StringComparison.OrdinalIgnoreCase)) ?? "Employee";

            var (hash, salt) = PasswordHasher.HashPassword(model.Password);

            // Tie the account to an existing employee record when we can match
            // one by employee number or email, so the two are the same person.
            var linkedEmployeeId = await _accountLinker.ResolveEmployeeIdAsync(
                email, model.BiometricUserId);

            var newUser = new ApiUser
            {
                Username = username,
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role,
                EmployeeId = linkedEmployeeId,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _context.ApiUsers.Add(newUser);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Admin {Admin} created user {Username} with role {Role} (employee link: {EmployeeId})",
                User.Identity?.Name, username, role, linkedEmployeeId);

            // The admin stays signed in as themselves — the new account is for
            // someone else. Note whether the employee link was made.
            TempData["SuccessMessage"] = linkedEmployeeId is null
                ? $"Account '{username}' created. No employee record matched — link it later from the Employees screen."
                : $"Account '{username}' created and linked to an employee record.";

            return RedirectToAction(nameof(Register));
        }

        // ═══════════════════════════════════════════════════════
        // LOGOUT
        // ═══════════════════════════════════════════════════════

        [HttpPost]
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name ?? "Unknown";
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User {Username} signed out.", username);

            TempData["InfoMessage"] = "You have been signed out safely.";
            return RedirectToAction("Login", "Account");
        }

        // ═══════════════════════════════════════════════════════
        // PROFILE & SETTINGS
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Login");
            }

            var user = await _context.ApiUsers
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("Login");
            }

            Employee? emp = null;
            if (user.EmployeeId.HasValue)
            {
                emp = await _context.Employees
                    .Include(e => e.Department)
                    .FirstOrDefaultAsync(e => e.EmployeeId == user.EmployeeId.Value);
            }

            var model = new UserProfileViewModel
            {
                UserId = user.ApiUserId,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedDate = user.CreatedDate,
                LastLoginDate = user.LastLoginDate,
                EmployeeName = emp?.EmployeeName,
                DepartmentName = emp?.Department?.DepartmentName
            };

            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please correct the form errors and try again.";
                return RedirectToAction("Profile");
            }

            var username = User.Identity?.Name;
            var user = await _context.ApiUsers.FirstOrDefaultAsync(u => u.Username.ToLower() == (username ?? "").ToLower());

            if (user == null)
            {
                return RedirectToAction("Login");
            }

            if (!PasswordHasher.VerifyPassword(model.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            {
                TempData["ErrorMessage"] = "Current password does not match our records.";
                return RedirectToAction("Profile");
            }

            var (hash, salt) = PasswordHasher.HashPassword(model.NewPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password updated successfully!";
            return RedirectToAction("Profile");
        }

        // ═══════════════════════════════════════════════════════
        // ACCESS DENIED
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            // An employee who followed a management link just gets sent to the
            // one screen they are allowed to see, rather than a dead end.
            if (User.Identity?.IsAuthenticated == true
                && string.Equals(User.FindFirstValue(ClaimTypes.Role), "Employee", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("My", "Attendance");
            }

            return View();
        }

        // ═══════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════

        private IActionResult RedirectToLocal(string? returnUrl, string? role = null)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Employee accounts have no access to the management dashboard;
            // send them straight to their own attendance log.
            if (string.Equals(role, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("My", "Attendance");
            }

            return RedirectToAction("Index", "Dashboard");
        }
    }
}
