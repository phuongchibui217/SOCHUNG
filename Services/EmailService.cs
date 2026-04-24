using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ExpenseManagerAPI.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
    {
        var smtpHost = _config["Email:SmtpHost"] ?? throw new InvalidOperationException("Email:SmtpHost chưa được cấu hình.");
        var smtpPort = _config.GetValue<int>("Email:SmtpPort", 587);
        var smtpUser = _config["Email:Username"];
        var smtpPass = _config["Email:Password"];
        var fromName = _config["Email:FromName"] ?? "SoChung";

        if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
        {
            _logger.LogError("[EmailService] SMTP credentials chưa được cấu hình. Set Email__Username và Email__Password qua environment variables.");
            throw new InvalidOperationException("SMTP credentials chưa được cấu hình.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, smtpUser));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Đặt lại mật khẩu";

        message.Body = new TextPart("html")
        {
            Text = $"""
                <p>Bạn đã yêu cầu đặt lại mật khẩu.</p>
                <p>Nhấn vào link bên dưới để đặt lại mật khẩu (có hiệu lực trong 3 phút):</p>
                <p><a href="{resetLink}">{resetLink}</a></p>
                <p>Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>
                """
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.Auto);
        await client.AuthenticateAsync(smtpUser, smtpPass);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Đã gửi email đặt lại mật khẩu tới {Email}", toEmail);
    }
}
