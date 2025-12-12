using StackExchange.Redis;
using System;

public class RedisOtpStore : IOtpStore
{
    private readonly IDatabase? _db;

    public RedisOtpStore()
    {
        var url = Environment.GetEnvironmentVariable("UPSTASH_REDIS_URL");
        if (string.IsNullOrEmpty(url))
        {
            // fallback to a simple in-memory store when UPSTASH_REDIS_URL is not set
            _inMemory = new System.Collections.Concurrent.ConcurrentDictionary<string, (string value, DateTime expires)>();
            _db = null; 
            return;
        }

        var config = ConfigurationOptions.Parse(url);
        config.Ssl = true;
        config.AbortOnConnectFail = false;
        var conn = ConnectionMultiplexer.Connect(config);
        _db = conn.GetDatabase();
    }

    // In-memory fallback
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string value, DateTime expires)>? _inMemory;

    public async Task SetOtpAsync(string key, string otp, TimeSpan ttl)
    {
        if (_inMemory != null)
        {
            _inMemory[key] = (otp, DateTime.UtcNow.Add(ttl));
            return;
        }
        await _db.StringSetAsync(key, otp, ttl);
    }

    public Task<string?> GetOtpAsync(string key)
    {
        if (_inMemory != null)
        {
            if (_inMemory.TryGetValue(key, out var v) && v.expires > DateTime.UtcNow) return Task.FromResult<string?>(v.value);
            return Task.FromResult<string?>(null);
        }
        return _db.StringGetAsync(key).ContinueWith<string?>(t => (string?)t.Result);
    }

    public Task DeleteOtpAsync(string key)
    {
        if (_inMemory != null)
        {
            _inMemory.TryRemove(key, out _);
            return Task.CompletedTask;
        }
        return _db.KeyDeleteAsync(key);
    }

    public async Task<int> IncrementOtpRequestsAsync(string key, TimeSpan window)
    {
        if (_inMemory != null)
        {
            var counterKey = "cnt:" + key;
            var now = DateTime.UtcNow;
            var entry = _inMemory.GetOrAdd(counterKey, ("1", now.Add(window)));
            // naive in-memory increment
            var current = 1;
            if (_inMemory.TryGetValue(counterKey, out var cur) && cur.expires > now)
            {
                current = int.Parse(cur.value) + 1;
                _inMemory[counterKey] = (current.ToString(), cur.expires);
            }
            else
            {
                _inMemory[counterKey] = ("1", now.Add(window));
            }
            return current;
        }

        var cnt = await _db.StringIncrementAsync(key);
        if (cnt == 1)
        {
            await _db.KeyExpireAsync(key, window);
        }
        return (int)cnt;
    }
}
