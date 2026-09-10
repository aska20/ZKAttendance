using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Common
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string bodyHtml, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("Cannot send email: recipient address is empty");
                return;
            }

            var enabled = _config.GetValue("SmtpSettings:Enabled", false);
            var host = _config["SmtpSettings:Host"];

            if (!enabled || string.IsNullOrWhiteSpace(host))
            {
                _logger.LogInformation(
                    "[Simulated Email] To: {To} | Subject: {Subject} | Body length: {Length} chars. Configure SmtpSettings:Host and SmtpSettings:Enabled=true in appsettings.json to send real emails.",
                    toEmail, subject, bodyHtml.Length);
                return;
            }

            var port = _config.GetValue("SmtpSettings:Port", 587);
            var username = _config["SmtpSettings:Username"];
            var password = _config["SmtpSettings:Password"];
            var fromEmail = _config["SmtpSettings:FromEmail"] ?? "noreply@zkattendance.local";
            var fromName = _config["SmtpSettings:FromName"] ?? "ZKAttendance System";
            var enableSsl = _config.GetValue("SmtpSettings:EnableSsl", true);

            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    client.Credentials = new NetworkCredential(username, password);
                }

                using var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = bodyHtml,
                    IsBodyHtml = true
                };

                message.To.Add(toEmail);

                await client.SendMailAsync(message, ct);
                _logger.LogInformation("Email successfully sent to {To} with subject '{Subject}'", toEmail, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To} with subject '{Subject}'", toEmail, subject);
                throw; // Rethrow so Hangfire captures the error and triggers retry policy
            }
        }
    }
}
