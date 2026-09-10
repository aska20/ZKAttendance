using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Infrastructure.Persistence;

namespace ZKAttendance.Infrastructure.Services.Notifications
{
    /// <summary>
    /// SMTP mail using System.Net.Mail, which ships with the framework.
    ///
    /// Settings live in the SystemSettings table under category "Email", not in
    /// appsettings.json, for the same reason the attendance rules do: an admin
    /// can change the mail server without a redeploy, and the file stays
    /// untouched.
    ///
    /// Keys (all optional until you actually send):
    ///   Email.SmtpHost      smtp.gmail.com
    ///   Email.SmtpPort      587
    ///   Email.UseSsl        true
    ///   Email.Username      sender@company.com
    ///   Email.Password      app password, NOT the account password
    ///   Email.FromAddress   noreply@company.com
    ///   Email.FromName      Attendance System
    ///
    /// The password sits in a normal column. That is honest rather than good:
    /// see docs/SECURITY-REVIEW.md. Use an app-specific password with send-only
    /// scope so a leak cannot read the mailbox.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        public const string Category = "Email";
        public const string CacheKey = "email_settings";

        private readonly AttendanceDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(
            AttendanceDbContext db,
            IMemoryCache cache,
            ILogger<SmtpEmailSender> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        private sealed class EmailSettings
        {
            public string? Host { get; set; }
            public int Port { get; set; } = 587;
            public bool UseSsl { get; set; } = true;
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? FromAddress { get; set; }
            public string FromName { get; set; } = "Attendance System";

            public bool IsUsable => !string.IsNullOrWhiteSpace(Host)
                                    && !string.IsNullOrWhiteSpace(FromAddress);
        }

        private async Task<EmailSettings> LoadAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue<EmailSettings>(CacheKey, out var cached) && cached is not null)
                return cached;

            var rows = await _db.SystemSettings
                .Where(s => s.Category == Category && s.IsActive)
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct);

            string? V(string k) => rows.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

            var settings = new EmailSettings
            {
                Host = V("SmtpHost"),
                Port = int.TryParse(V("SmtpPort"), out var p) ? p : 587,
                UseSsl = !string.Equals(V("UseSsl"), "false", StringComparison.OrdinalIgnoreCase),
                Username = V("Username"),
                Password = V("Password"),
                FromAddress = V("FromAddress") ?? V("Username"),
                FromName = V("FromName") ?? "Attendance System"
            };

            _cache.Set(CacheKey, settings, TimeSpan.FromMinutes(5));
            return settings;
        }

        public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
            => (await LoadAsync(ct)).IsUsable;

        public async Task SendAsync(
            string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            var s = await LoadAsync(ct);

            if (!s.IsUsable)
                throw new InvalidOperationException(
                    "Email is not configured. Set the SMTP host and from-address in Settings.");

            if (string.IsNullOrWhiteSpace(to))
                throw new ArgumentException("No recipient address.", nameof(to));

            using var client = new SmtpClient(s.Host, s.Port)
            {
                EnableSsl = s.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                // Without this the send can hang until the request times out,
                // which on a 200-employee run means the whole job stalls.
                Timeout = 20000
            };

            if (!string.IsNullOrWhiteSpace(s.Username))
                client.Credentials = new NetworkCredential(s.Username, s.Password);

            using var message = new MailMessage
            {
                From = new MailAddress(s.FromAddress!, s.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            await client.SendMailAsync(message, ct);
            _logger.LogDebug("Sent '{Subject}' to {To}", subject, to);
        }

        /// <summary>Drop the cache after a settings change.</summary>
        public void InvalidateCache() => _cache.Remove(CacheKey);
    }
}
