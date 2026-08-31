using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Infrastructure.Services.Attendances;
using ZKAttendance.Application.Services.Attendances;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.Services.Shifts;
using ZKAttendance.Infrastructure.NepaliCalendar;
using ZKAttendance.Infrastructure.Services.Employees;
using ZKAttendance.Infrastructure.Services.Branches;
using ZKAttendance.Infrastructure.Services.Departments;
using ZKAttendance.Infrastructure.Services.WorkShifts;
using ZKAttendance.Infrastructure.Services.Devices;
using ZKAttendance.Infrastructure.Services.Report;
using ZKAttendance.Infrastructure.Services.Common;
using ZKAttendance.Infrastructure.Devices;
using ZKAttendance.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;



var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════
// MVC + JSON Settings
// ═══════════════════════════════════════════════════════
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
    });

// ═══════════════════════════════════════════════════════
// Database Configuration
// ═══════════════════════════════════════════════════════
builder.Services.AddDbContext<AttendanceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ═══════════════════════════════════════════════════════
// Memory Cache
// ═══════════════════════════════════════════════════════
builder.Services.AddMemoryCache();

// ═══════════════════════════════════════════════════════
// Repositories
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<BranchRepository>();
builder.Services.AddScoped<DeviceRepository>();
builder.Services.AddScoped<EmployeeRepository>();
builder.Services.AddScoped<DepartmentRepository>();
builder.Services.AddScoped<WorkShiftRepository>();
builder.Services.AddScoped<AttendanceLogRepository>();

// ═══════════════════════════════════════════════════════
// Core Services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<IBrancheService, BrancheService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IWorkShiftService, WorkShiftService>();

// ═══════════════════════════════════════════════════════
// Attendance Services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<AttendanceCalculationService>();
builder.Services.AddScoped<AttendanceQueryService>();

// ═══════════════════════════════════════════════════════
// Shift Services
// ═══════════════════════════════════════════════════════
// Port -> adapter. AttendanceCalculationService (Application) depends on the
// interface; the EF Core implementation lives in Infrastructure.
builder.Services.AddScoped<IShiftAssignmentService, ShiftAssignmentService>();

// ═══════════════════════════════════════════════════════
// Report Services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ExcelReportService>();
builder.Services.AddScoped<PdfReportService>();

// ═══════════════════════════════════════════════════════
// Common Services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<LookupService>();

// API-only services
builder.Services.AddScoped<IEmployeeDeviceEnrollment, EmployeeDeviceEnrollment>();
builder.Services.AddScoped<IManualAttendanceEntry, ManualAttendanceEntry>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();

// Keeps a login account (ApiUser) and its Employee record pointing at one person.
builder.Services.AddScoped<EmployeeAccountLinker>();

// ═══════════════════════════════════════════════════════
// Background Services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<IDeviceMonitorService, DeviceMonitorService>();
builder.Services.AddHostedService<DeviceMonitorBackgroundService>();

// ═══════════════════════════════════════════════════════
// Nepali Calendar (NEW)
// Singleton: the month-length table never changes at run time.
// The constructor runs SelfTest(), so a wrong calendar table
// fails the application at startup rather than producing
// quietly wrong reports.
// ═══════════════════════════════════════════════════════
var weeklyOff = Enum.Parse<DayOfWeek>(
    builder.Configuration["NepaliCalendarSettings:WeeklyOffDay"] ?? "Saturday");

// INepaliCalendar is the port Application depends on.
// NepaliCalendarAdapter wraps the converter that lives in Infrastructure.
builder.Services.AddScoped<INepaliCalendar, NepaliCalendarAdapter>();
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddSingleton<INepaliDateService>(
    _ => new NepaliDateService(weeklyOff));

// ═══════════════════════════════════════════════════════
// Multi-device attendance collection (NEW)
//
// The reader is resolved through a factory because each device
// needs its own short-lived SDK session, and because the fake
// implementation lets the whole pipeline run with no hardware.
// ═══════════════════════════════════════════════════════
var useFakeReader = builder.Configuration
    .GetValue("SyncConfiguration:UseFakeDeviceReader", true);

builder.Services.AddTransient<Func<IZkDeviceReader>>(sp => () =>
{
    if (useFakeReader)
        return new FakeDeviceReader(sp.GetRequiredService<ILogger<FakeDeviceReader>>());

    // Real hardware: implement ZkemkeeperDeviceReader against zkemkeeper.dll,
    // set the project platform to x86, then flip UseFakeDeviceReader to false.
    throw new NotImplementedException(
        "Register zkemkeeper.dll and add ZkemkeeperDeviceReader, " +
        "or set SyncConfiguration:UseFakeDeviceReader to true.");
});

builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.AddHostedService<AttendanceSyncBackgroundService>();

// ═══════════════════════════════════════════════════════
// Authentication: Cookie (Web MVC) + JWT Bearer (API)
// ═══════════════════════════════════════════════════════
var jwtKey = builder.Configuration["JwtSettings:Key"]
    ?? throw new InvalidOperationException("JwtSettings:Key missing from appsettings.json");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "ZKAttendance.Auth";
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        // Default is 5 minutes of grace, which makes short-lived tokens
        // behave unexpectedly during testing.
        ClockSkew = TimeSpan.Zero
    };
});

// ═══════════════════════════════════════════════════════
// API Documentation (Swagger)
// ═══════════════════════════════════════════════════════
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ZKAttendance API",
        Version = "v1",
        Description = "Configuration, device and attendance endpoints for the biometric attendance system."
    });

    // Pull the <summary> comments from the generated XML doc file into the UI.
    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);

    // Adds the Authorize button. Paste an accessToken from /api/Auth/login
    // and Swagger sends it as a Bearer header on every subsequent call.
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the accessToken value only - Swagger adds the 'Bearer ' prefix."
    });

    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ═══════════════════════════════════════════════════════
// Build Application
// ═══════════════════════════════════════════════════════
var app = builder.Build();

// ═══════════════════════════════════════════════════════
// Database Initialization & Seed
// ═══════════════════════════════════════════════════════
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var dbContext = services.GetRequiredService<AttendanceDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        await DbInitializer.InitializeAsync(dbContext, logger);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during database startup initialization.");
    }
}

// ═══════════════════════════════════════════════════════
// Configure HTTP Pipeline
// ═══════════════════════════════════════════════════════
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "ZKAttendance API v1");
        // Serve the UI at /swagger (the default). Set RoutePrefix to ""
        // to put it at the site root instead.
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "ZKAttendance API";
    });
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// MVC pages: /Attendance/Index, /Devices/Create, and so on.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// API controllers use attribute routing ([Route("api/[controller]")]).
// MapControllerRoute above does NOT map those - it only handles the
// {controller}/{action} convention. Without this line every /api/... URL
// returns 404 even though the controller exists and compiles, and Swagger
// shows an empty page because no API endpoints are registered.
app.MapControllers();

app.Run();
