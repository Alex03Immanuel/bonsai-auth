using Microsoft.AspNetCore.Mvc;
using Serilog;
using BCrypt.Net;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IUserStore _users;
    private readonly IOtpStore _otp;
    private readonly IEmailService _email;

    public AuthController(IUserStore users, IOtpStore otp, IEmailService email)
    {
        _users = users;
        _otp = otp;
        _email = email;
    }

    // ------------------------
    // REGISTER (email + password)
    // ------------------------
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (await _users.ExistsAsync(dto.Email))
        {
            Log.Warning("register_fail {email}", dto.Email);
            return Conflict(new { error = "user_exists" });
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        await _users.CreateAsync(dto.Email, hash);

        Log.Information("register_success {email}", dto.Email);
        return Ok(new { ok = true });
    }

    // ------------------------
    // REQUEST OTP
    // ------------------------
    [HttpPost("request-otp")]
    public async Task<IActionResult> RequestOtp(EmailDto dto)
    {
        if (!await _users.ExistsAsync(dto.Email))
        {
            Log.Warning("otp_request_fail_unknown_user {email}", dto.Email);
            return BadRequest(new { error = "unknown_user" });
        }

        // rate limit (max 5 OTPs per hour)
        var count = await _otp.IncrementOtpRequestsAsync(dto.Email, TimeSpan.FromHours(1));
        if (count > 5)
        {
            Log.Warning("otp_rate_limited {email} count={count}", dto.Email, count);
            return StatusCode(429, new { error = "too_many_requests" });
        }

        var otp = new Random().Next(100000, 999999).ToString();
        await _otp.SetOtpAsync($"otp:{dto.Email}", otp, TimeSpan.FromMinutes(5));

        await _email.SendOtpAsync(dto.Email, otp);
        Log.Information("otp_sent {email}", dto.Email);

        return Ok(new { ok = true });
    }

    // ------------------------
    // LOGIN (password OR otp)
    // ------------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var hash = await _users.GetPasswordHashAsync(dto.Email);
        if (hash == null)
        {
            Log.Warning("login_fail_unknown_user {email}", dto.Email);
            return BadRequest(new { error = "unknown_user" });
        }

        bool success = false;

        if (!string.IsNullOrEmpty(dto.Password))
        {
            success = BCrypt.Net.BCrypt.Verify(dto.Password, hash);
            if (!success)
            {
                Log.Warning("failed_login_password {email}", dto.Email);
                return Unauthorized(new { error = "invalid_password" });
            }
        }
        else if (!string.IsNullOrEmpty(dto.Otp))
        {
            var expected = await _otp.GetOtpAsync($"otp:{dto.Email}");
            if (expected != dto.Otp)
            {
                Log.Warning("failed_login_otp {email}", dto.Email);
                return Unauthorized(new { error = "invalid_otp" });
            }

            // delete OTP after successful login
            await _otp.DeleteOtpAsync($"otp:{dto.Email}");
            success = true;
        }
        else
        {
            return BadRequest(new { error = "missing_credentials" });
        }

        Log.Information("login_success {email}", dto.Email);
        return Ok(new { ok = true, token = "demo-token" });
    }
}

// DTOs
public record RegisterDto(string Email, string Password);
public record EmailDto(string Email);
public record LoginDto(string Email, string? Password, string? Otp);
