namespace AethericAdmin.Web.Maintenance.Jobs;

public interface ICredentialStore
{
    Task<SshCredential> AddPasswordAsync(
        string name,
        string username,
        string password,
        CancellationToken ct = default);

    Task<SshCredential> AddPrivateKeyAsync(
        string name,
        string username,
        string privateKey,
        string? passphrase,
        CancellationToken ct = default);

    Task<IReadOnlyList<SshCredential>> ListAsync(CancellationToken ct = default);

    Task<SshCredential?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Decrypts the credential's secret only for the duration of <paramref name="operation"/> - the
    /// raw secret never escapes as a return value a caller could log or hold onto. Mirrors
    /// aetheric-gm's ISshCredentialService.UsePrivateKeyAsync.
    /// </summary>
    Task<T> UseSecretAsync<T>(
        Guid id,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
