using Hangfire.Dashboard;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ZKAttendance.Api.Security
{
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

            // Always allow in development environment for inspection
            if (env.IsDevelopment())
            {
                return true;
            }

            // Check for optional dashboard access token in query string (useful for browser access)
            var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
            var dashboardToken = config["HangfireSettings:DashboardToken"];
            if (!string.IsNullOrWhiteSpace(dashboardToken)
                && httpContext.Request.Query.TryGetValue("token", out var queryToken)
                && queryToken == dashboardToken)
            {
                return true;
            }

            // Check for authenticated user with Management/Admin role
            var user = httpContext.User;
            return user.Identity?.IsAuthenticated == true && (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Hr));
        }
    }
}
