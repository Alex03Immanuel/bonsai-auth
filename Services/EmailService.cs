using MailKit.Net.Smtp;
using MimeKit;
using System;

public class EmailService : IEmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _pass;
    private readonly string _from;

    public EmailService()
    {
        _host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "";
        _port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 587;
        _user = Environment.GetEnvironmentVariable("SMTP_USER") ?? "";
        _pass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? "";
        _from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? _user;
    }

    public async Task SendOtpAsync(string to, string otp)
    {
        // fallback to console if SMTP is not configured
        if (string.IsNullOrEmpty(_host))
        {
            Console.WriteLine($"[DEV EMAIL] To: {to} OTP: {otp}");
            return;
        }

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(_from));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = "Your OTP code";
        msg.Body = new TextPart("plain") { Text = $"Your OTP is {otp}. Expires in 5 minutes." };

        using var client = new SmtpClient();
        await client.ConnectAsync(_host, _port, MailKit.Security.SecureSocketOptions.StartTls);

        if (!string.IsNullOrEmpty(_user))
            await client.AuthenticateAsync(_user, _pass);

        await client.SendAsync(msg);
        await client.DisconnectAsync(true);
    }
}
