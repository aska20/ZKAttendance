using Hangfire.Dashboard;

namespace ZKAttendance.Api.Security
{
    /// <summary>
    /// Guards the Hangfire dashboard.
    ///
    /// Hangfire allows EVERYONE when no filter is supplied. The dashboard can
    /// trigger jobs, delete them and read their arguments, so on a reachable
    /// host that means any visitor could fire the attendance email job at will.
    /// This restricts it to a signed-in Admin.
    ///
    /// Note the dashboard uses cookie/whatever the browser sends, not the JWT
    /// the SPA holds in memory. If your admins sign in only through the API,
    /// the dashboard will refuse them; reach it from a browser session, or
    /// restrict it at the reverse proxy instead.
    /// </summary>
    public class HangfireAdminFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var http = context.GetHttpContext();
            var user = http.User;

            if (user.Identity?.IsAuthenticated != true) return false;

            return user.IsInRole("Admin");
        }
    }
}
