using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace AethericAdmin.Web.Maintenance.Jobs;

/// <summary>
/// SSH credentials, persisted in MongoDB with the secret (password or private key) encrypted at
/// rest via a dedicated Data Protection purpose string - the Redis-backed key ring
/// configured by AddAdminRedisPersistence, so credentials survive restarts when that ring is retained. The secret is
/// only ever decrypted transiently inside UseSecretAsync's callback; it's never returned to the UI
/// or held in a variable outside that scope. Ported from aetheric-gm's SqliteSshCredentialService
/// (same protector pattern, same private-key validation via SshPrivateKeyInspector), backed by
/// MongoDB instead of SQLite to match this app's existing stack.
/// </summary>
public sealed class MongoCredentialStore : ICredentialStore
{
    private readonly IMongoCollection<SshCredential> _collection;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public MongoCredentialStore(
        [FromKeyedServices("Maintenance")] IMongoDatabase database,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _collection = database.GetCollection<SshCredential>("maintenance-credentials");
        _protector = dataProtectionProvider.CreateProtector("AethericAdmin.Web.Maintenance.SshCredentials");
        _timeProvider = timeProvider;
    }

    public async Task<SshCredential> AddPasswordAsync(
        string name,
        string username,
        string password,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var credential = new SshCredential(
            Guid.NewGuid(),
            name.Trim(),
            username.Trim(),
            SshCredentialSecretKind.Password,
            Protect(password),
            Algorithm: null,
            Fingerprint: null,
            RequiresPassphrase: false,
            _timeProvider.GetUtcNow());

        await _collection.InsertOneAsync(credential, cancellationToken: ct).ConfigureAwait(false);
        return credential;
    }

    public async Task<SshCredential> AddPrivateKeyAsync(
        string name,
        string username,
        string privateKey,
        string? passphrase,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var inspection = SshPrivateKeyInspector.Inspect(privateKey, passphrase);
        var credential = new SshCredential(
            Guid.NewGuid(),
            name.Trim(),
            username.Trim(),
            SshCredentialSecretKind.PrivateKey,
            Protect(privateKey.Trim()),
            inspection.Algorithm,
            inspection.Fingerprint,
            inspection.RequiresPassphrase,
            _timeProvider.GetUtcNow());

        await _collection.InsertOneAsync(credential, cancellationToken: ct).ConfigureAwait(false);
        return credential;
    }

    public async Task<IReadOnlyList<SshCredential>> ListAsync(CancellationToken ct = default)
    {
        return await _collection.Find(FilterDefinition<SshCredential>.Empty)
            .SortBy(credential => credential.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<SshCredential?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _collection.Find(credential => credential.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<T> UseSecretAsync<T>(
        Guid id,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var credential = await GetAsync(id, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("SSH credential was not found.");

        var secret = Unprotect(credential.ProtectedSecret);
        try
        {
            var result = await operation(secret, ct).ConfigureAwait(false);
            await _collection.UpdateOneAsync(
                    c => c.Id == id,
                    Builders<SshCredential>.Update.Set(c => c.LastUsedAt, _timeProvider.GetUtcNow()),
                    cancellationToken: ct)
                .ConfigureAwait(false);
            return result;
        }
        finally
        {
            secret = string.Empty;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(credential => credential.Id == id, ct).ConfigureAwait(false);
    }

    private byte[] Protect(string secret) => _protector.Protect(Encoding.UTF8.GetBytes(secret));

    private string Unprotect(byte[] protectedSecret)
    {
        var bytes = _protector.Unprotect(protectedSecret);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
