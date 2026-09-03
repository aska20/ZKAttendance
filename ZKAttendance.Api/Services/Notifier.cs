using Microsoft.EntityFrameworkCore;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Api.Services
{
    /// <summary>Creates in-app notifications. Thin helper over the Notifications table.</summary>
    public class Notifier
    {
        private readonly AttendanceDbContext _db;

        public Notifier(AttendanceDbContext db) => _db = db;

        /// <summary>Notify every user that holds a given role (e.g. "Admin").</summary>
        public async Task ToRoleAsync(string role, string message, string? linkPath = null, string? type = null)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientRole = role,
                Message = message,
                LinkPath = linkPath,
                Type = type,
                CreatedDate = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }

        /// <summary>Notify one specific user.</summary>
        public async Task ToUserAsync(int userId, string message, string? linkPath = null, string? type = null)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientUserId = userId,
                Message = message,
                LinkPath = linkPath,
                Type = type,
                CreatedDate = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
    }
}
