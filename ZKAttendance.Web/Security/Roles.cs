namespace ZKAttendance.Web.Security
{
    /// <summary>
    /// The three account roles and the groupings the app authorizes against.
    ///
    /// Admin and HR are treated identically for access purposes ("management").
    /// Employee accounts can only see their own attendance and their profile.
    /// </summary>
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Hr = "HR";
        public const string Employee = "Employee";

        /// <summary>Use in [Authorize(Roles = Roles.Management)] on management screens.</summary>
        public const string Management = Admin + "," + Hr;
    }
}
