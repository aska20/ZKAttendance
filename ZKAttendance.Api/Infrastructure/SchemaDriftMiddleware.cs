using Microsoft.EntityFrameworkCore;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Infrastructure
{
    /// <summary>
    /// Turns "the database is behind the code" into a sentence instead of a 500.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// When an entity gains a property, EF immediately starts selecting that
    /// column. If the migration has not been applied, SQL Server answers
    /// "Invalid column name 'LocalServerId'" and ASP.NET turns that into a bare
    /// 500 with no hint. Every screen that touches Devices or Departments then
    /// breaks at once and it looks like the whole application is broken, when
    /// one command would fix it.
    ///
    /// This catches exactly that family of error and says what to run.
    /// Everything else passes through untouched.
    /// </summary>
    public class SchemaDriftMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SchemaDriftMiddleware> _logger;

        public SchemaDriftMiddleware(RequestDelegate next, ILogger<SchemaDriftMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex) when (IsSchemaDrift(ex, out var missing))
            {
                _logger.LogError(ex,
                    "Database is behind the code: {Missing}. Run Update-Database.", missing);

                // 503, not 500: the server is fine, it just is not ready.
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    message =
                        $"The database is missing {missing}. The code expects a newer schema. " +
                        "Run the migrations and restart.",
                    fix = new[]
                    {
                        "Add-Migration AddAttendanceApprovals",
                        "Add-Migration AddLocalServersAndDeptShift",
                        "Update-Database"
                    },
                    note = "Run Migrations/CheckDuplicateEmails.sql before the first one if two employees share an email."
                });
            }
        }

        /// <summary>
        /// True for a missing table or column. Matched on the message because
        /// the SQL Server error numbers (207, 208) arrive wrapped in a
        /// DbUpdateException or an EF query exception depending on the path.
        /// </summary>
        internal static bool IsSchemaDrift(Exception ex, out string missing)
        {
            missing = "";

            for (var e = ex; e is not null; e = e.InnerException)
            {
                var m = e.Message;

                if (m.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
                {
                    missing = ExtractQuoted(m, "a column") + " (column)";
                    return true;
                }

                if (m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
                {
                    missing = ExtractQuoted(m, "a table") + " (table)";
                    return true;
                }
            }

            return false;
        }

        private static string ExtractQuoted(string message, string fallback)
        {
            var start = message.IndexOf('\'');
            if (start < 0) return fallback;
            var end = message.IndexOf('\'', start + 1);
            return end < 0 ? fallback : message[(start + 1)..end];
        }
    }

    public static class SchemaCheck
    {
        /// <summary>
        /// Checks the schema once at startup and writes a loud, readable
        /// warning if it is behind. Far better than discovering it one broken
        /// screen at a time.
        ///
        /// Deliberately does NOT stop the app. Some screens work fine without
        /// the new tables, and refusing to start would be worse than degraded.
        /// </summary>
        public static async Task WarnIfBehindAsync(IServiceProvider services, ILogger logger)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            try
            {
                if (!await db.Database.CanConnectAsync())
                {
                    logger.LogError(
                        "Cannot connect to the database. Check ConnectionStrings:DefaultConnection.");
                    return;
                }

                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count > 0)
                {
                    logger.LogWarning(
                        "{Count} migration(s) have not been applied: {List}. Run Update-Database.",
                        pending.Count, string.Join(", ", pending));
                }

                // A migration can exist and still not cover a property added
                // after it was generated, so probe the actual columns too.
                var problems = new List<string>();

                foreach (var (label, probe) in new (string, Func<Task>)[]
                {
                    ("Devices.LocalServerId", async () => await db.Devices.Select(d => d.LocalServerId).FirstOrDefaultAsync()),
                    ("Departments.DefaultShiftId", async () => await db.Departments.Select(d => d.DefaultShiftId).FirstOrDefaultAsync()),
                    ("AttendanceApprovals", async () => await db.AttendanceApprovals.AnyAsync()),
                    ("LocalServers", async () => await db.LocalServers.AnyAsync()),
                })
                {
                    try { await probe(); }
                    catch (Exception ex) when (SchemaDriftMiddleware.IsSchemaDrift(ex, out _))
                    {
                        problems.Add(label);
                    }
                    catch { /* not a schema problem; leave it to the request path */ }
                }

                if (problems.Count > 0)
                {
                    logger.LogError(
                        "═══ DATABASE IS BEHIND THE CODE ═══\n" +
                        "Missing: {Problems}\n" +
                        "Screens touching these will return 503 until you run:\n" +
                        "  Add-Migration AddAttendanceApprovals\n" +
                        "  Add-Migration AddLocalServersAndDeptShift\n" +
                        "  Update-Database\n" +
                        "═══════════════════════════════════",
                        string.Join(", ", problems));
                }
                else
                {
                    logger.LogInformation("Database schema is up to date.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Schema check could not run");
            }
        }
    }
}
