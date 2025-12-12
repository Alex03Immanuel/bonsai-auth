public interface IOtpStore
{
    Task SetOtpAsync(string key, string otp, TimeSpan ttl);
    Task<string?> GetOtpAsync(string key);
    Task DeleteOtpAsync(string key);
    Task<int> IncrementOtpRequestsAsync(string key, TimeSpan window);
}
