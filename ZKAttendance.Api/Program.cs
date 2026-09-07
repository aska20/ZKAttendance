using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;
using ZKAttendance.Infrastructure.Persistence.Repositories;
using ZKAttendance.Infrastructure.Services.Attendances;
using ZKAttendance.Application.Services.Attendances;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Infrastructure.NepaliCalendar;
using ZKAttendance.Infrastructure.Services.Employees;
using ZKAttendance.Infrastructure.Services.Branches;
using ZKAttendance.Infrastructure.Services.Departments;
using ZKAttendance.Infrastructure.Services.Devices;
using ZKAttendance.Infrastructure.Services.Report;
using ZKAttendance.Infrastructure.Services.Common;
using ZKAttendance.Infrastructure.Devices;
using ZKAttendance.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Allow large request bodies for base-64 employee photo uploads (up to 10 MB).
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
});


// ═══════════════════════════════════════════════════════
// Controllers + JSON settings (API only — no Razor views)
// ═══════════════════════════════════════════════════════
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
    });

// ═══════════════════════════════════════════════════════
// CORS — the React SPA runs on its own origin (Vite dev server,
// or a static host in production). Origins come from configuration
// so deployment does not need a rebuild.
// ═══════════════════════════════════════════════════════
const string SpaCorsPolicy = "SpaCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy(SpaCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ═══════════════════════════════════════════════════════
// Database
// ═══════════════════════════════════════════════════════
builder.Services.AddDbContext<AttendanceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddMemoryCache();

// ═══════════════════════════════════════════════════════
// Repositories
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<BranchRepository>();
builder.Services.AddScoped<DeviceRepository>();
builder.Services.AddScoped<EmployeeRepository>();
builder.Services.AddScoped<DepartmentRepository>();
builder.Services.AddScoped<AttendanceLogRepository>();

// ═══════════════════════════════════════════════════════
// Core services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<IBrancheService, BrancheService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();

// Attendance
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<AttendanceCalculationService>();
builder.Services.AddScoped<AttendanceQueryService>();

// Reports
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ExcelReportService>();
builder.Services.AddScoped<PdfReportService>();

// Common
builder.Services.AddScoped<LookupService>();

// API-only services
builder.Services.AddScoped<IEmployeeDeviceEnrollment, EmployeeDeviceEnrollment>();
builder.Services.AddScoped<IManualAttendanceEntry, ManualAttendanceEntry>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();


// In-app notifications (bell menu).
builder.Services.AddScoped<ZKAttendance.Api.Services.Notifier>();

// ═══════════════════════════════════════════════════════
// Background services
// ═══════════════════════════════════════════════════════
builder.Services.AddScoped<IDeviceMonitorService, DeviceMonitorService>();
builder.Services.AddHostedService<DeviceMonitorBackgroundService>();

// ═══════════════════════════════════════════════════════
// Nepali calendar
// ═══════════════════════════════════════════════════════
var weeklyOff = Enum.Parse<DayOfWeek>(
    builder.Configuration["NepaliCalendarSettings:WeeklyOffDay"] ?? "Saturday");

builder.Services.AddScoped<INepaliCalendar, NepaliCalendarAdapter>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<INepaliDateService>(_ => new NepaliDateService(weeklyOff));

// ═══════════════════════════════════════════════════════
// Multi-device attendance collection
//
// SyncConfiguration:DeviceProtocol picks the IZkDeviceReader:
//   "Fake" (default) — FakeDeviceReader, generates believable punches, no hardware
//   "Tcp"            — ZkTcpDeviceReader, real ZKTeco devices over TCP :4370, no DLL
//
// The legacy "UseFakeDeviceReader": false also selects "Tcp".
// ═══════════════════════════════════════════════════════
var legacyFakeFlag = builder.Configuration
    .GetValue("SyncConfiguration:UseFakeDeviceReader", true);
var deviceProtocol = builder.Configuration["SyncConfiguration:DeviceProtocol"]
    ?? (legacyFakeFlag ? "Fake" : "Tcp");
var deviceCommPassword = builder.Configuration
    .GetValue("SyncConfiguration:DeviceCommPassword", 0);
var deviceTimeoutMs = builder.Configuration
    .GetValue("SyncConfiguration:DeviceTimeoutMs", 5000);

builder.Services.AddTransient<Func<IZkDeviceReader>>(sp => () =>
    deviceProtocol.Equals("Tcp", StringComparison.OrdinalIgnoreCase)
        ? new ZkTcpDeviceReader(
            sp.GetRequiredService<ILogger<ZkTcpDeviceReader>>(),
            deviceCommPassword,
            deviceTimeoutMs)
        : new FakeDeviceReader(sp.GetRequiredService<ILogger<FakeDeviceReader>>()));

builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.AddHostedService<AttendanceSyncBackgroundService>();

// Multi-device enrolment. One ACTIVE device is the Master: the only place a
// finger is physically captured. Everything else is a Slave and receives its
// users by being written to from here.
//
// No background service and no new settings - propagation runs only when it is
// asked for, from the employee screen or the Devices page.
builder.Services.AddScoped<IDeviceEnrollmentOrchestrator, DeviceEnrollmentOrchestrator>();

// ═══════════════════════════════════════════════════════
// Authentication: JWT Bearer only.
// The SPA holds a short-lived access token and a rotating refresh token;
// there is no server-rendered page, so no cookie scheme.
// ═══════════════════════════════════════════════════════
var jwtKey = builder.Configuration["JwtSettings:Key"]
    ?? throw new InvalidOperationException("JwtSettings:Key missing from appsettings.json");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ═══════════════════════════════════════════════════════
// Swagger
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

    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);

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
// Build
// ═══════════════════════════════════════════════════════
var app = builder.Build();

// Database initialization & seed
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
// HTTP pipeline
// ═══════════════════════════════════════════════════════
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "ZKAttendance API v1");
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "ZKAttendance API";
    });
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors(SpaCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// This is a headless API — there is no home page. Point a browser that lands
// on the root at the Swagger UI (Development) so it is not just a bare 404.
app.MapGet("/", () => Results.Redirect("/swagger"))
   .ExcludeFromDescription();

app.Run();
