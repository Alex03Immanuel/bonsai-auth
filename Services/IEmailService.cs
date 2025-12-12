public interface IEmailService
{
    Task SendOtpAsync(string to, string otp);
}
