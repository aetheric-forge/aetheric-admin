using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using StackExchange.Redis;

namespace AethericAdmin.Web.Infrastructure;

/// <summary>Persistent Workbench values; subscriptions remain local to this process.</summary>
public sealed class RedisWorkbenchService(IDatabase database, string keyPrefix) : IWorkbenchService
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Func<object?, CancellationToken, Task>>> _receivers = new();
    private readonly string _prefix = !string.IsNullOrWhiteSpace(keyPrefix)
        ? keyPrefix : throw new ArgumentException("A Workbench key prefix is required.", nameof(keyPrefix));

    public async Task PutAsync<TWork>(object key, TWork work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();
        var redisKey = Key<TWork>(key);
        // No expiry: run history is durable state, not a cache.
        // Once sent, finish the write before releasing the caller's read/modify/write gate.
        await database.StringSetAsync(redisKey, JsonSerializer.Serialize(work));
        if (_receivers.TryGetValue(typeof(TWork), out var receivers))
            foreach (var receiver in receivers.Values)
                await receiver(work, ct);
    }

    public async Task<TWork?> GetAsync<TWork>(object key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = await database.StringGetAsync(Key<TWork>(key)).WaitAsync(ct);
        // Malformed existing data must not be treated as an empty ledger and overwritten.
        if (value.IsNull) return default;
        return JsonSerializer.Deserialize<TWork>((string)value!)
            ?? throw new InvalidDataException("The persisted Workbench value is null.");
    }

    public IDisposable Subscribe<TWork>(Func<TWork, CancellationToken, Task> receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        var id = Guid.NewGuid();
        var receivers = _receivers.GetOrAdd(typeof(TWork), _ => new());
        receivers[id] = (work, ct) => receiver((TWork)work!, ct);
        return new Subscription(() => receivers.TryRemove(id, out _));
    }

    private RedisKey Key<TWork>(object key)
    {
        // Current consumers use string keys. Reject arbitrary object.ToString() collisions explicitly.
        if (key is not string text || string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Redis Workbench requires a non-empty string key.", nameof(key));
        // Type.ToString excludes assembly versions, including for generic ledger types.
        var identity = JsonSerializer.Serialize(new[] { typeof(TWork).ToString(), text });
        return _prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
