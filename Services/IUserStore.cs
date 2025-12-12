public interface IUserStore
{
    Task<bool> ExistsAsync(string email);
    Task CreateAsync(string email, string passwordHash);
    Task<string?> GetPasswordHashAsync(string email);
}
