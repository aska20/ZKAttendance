namespace ZKAttendance.Application.Abstractions
{
    public interface IEmailSender
    {
        /// <summary>
        /// Sends an email asynchronously. If email is not configured, implementations
        /// should log safely without crashing the background worker.
        /// </summary>
        Task SendEmailAsync(string toEmail, string subject, string bodyHtml, CancellationToken ct = default);
    }
}
