using System.Collections.Concurrent;

public class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<bool> ExistsAsync(string email) => Task.FromResult(_store.ContainsKey(email));

    public Task CreateAsync(string email, string passwordHash)
    {
        _store[email] = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(string email) =>
        Task.FromResult(_store.TryGetValue(email, out var h) ? h : null);
}
