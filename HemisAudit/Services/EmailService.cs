using System.Net;
using System.Net.Mail;

namespace HemisAudit.Services
{
    public interface IEmailService
    {
        Task SendAsync(string to, string subject, string body, bool isHtml = true);
    }

    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string body, bool isHtml = true)
        {
            var host = _configuration["Email:SmtpHost"];
            var port = _configuration.GetValue<int?>("Email:SmtpPort");
            var user = _configuration["Email:SmtpUser"];
            var pass = _configuration["Email:SmtpPass"];
            var fromAddress = _configuration["Email:FromAddress"] ?? _configuration["Email:From"];
            var fromName = _configuration["Email:FromName"] ?? "HEMIS Audit System";

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
            {
                _logger.LogInformation("Email not sent to {To}. Subject: {Subject}. Body: {Body}", to, subject, body);
                return;
            }

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };

                message.To.Add(to);

                using var client = new SmtpClient(host, port ?? 587)
                {
                    EnableSsl = true,
                    Credentials = string.IsNullOrWhiteSpace(user)
                        ? CredentialCache.DefaultNetworkCredentials
                        : new NetworkCredential(user, pass)
                };

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email send failed for {To}. Falling back to log only.", to);
                _logger.LogInformation("Reset email for {To}: {Subject} | {Body}", to, subject, body);
            }
        }
    }
}
