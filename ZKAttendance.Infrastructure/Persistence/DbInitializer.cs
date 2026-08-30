using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Security;

namespace ZKAttendance.Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(AttendanceDbContext context, ILogger logger)
        {
            try
            {
                // Ensure database is created / migrated if supported
                if (!await context.ApiUsers.AnyAsync())
                {
                    logger.LogInformation("No existing users found in ApiUsers. Seeding default administrator account...");

                    var (hash, salt) = PasswordHasher.HashPassword("Admin@123");

                    var defaultAdmin = new ApiUser
                    {
                        Username = "admin",
                        Email = "admin@zkattendance.local",
                        PasswordHash = hash,
                        PasswordSalt = salt,
                        Role = "Admin",
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    };

                    context.ApiUsers.Add(defaultAdmin);
                    await context.SaveChangesAsync();

                    logger.LogInformation("Default administrator account 'admin' created successfully with password 'Admin@123'.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding initial database authentication records.");
            }
        }
    }
}
