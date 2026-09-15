namespace AethericAdmin.Web.Maintenance.Jobs;

/// <summary>
/// Never carries the raw secret - only a fingerprint/algorithm (for private keys, derived once at
/// import time via SshPrivateKeyInspector) so admins can identify a credential without ever seeing
/// it again. Mirrors the shape of AethericGm.Core.Profiles.SshCredential (aetheric-gm), adapted to
/// also cover plain passwords, not just private keys.
/// </summary>
public sealed record SshCredential(
    Guid Id,
    string Name,
    string Username,
    SshCredentialSecretKind SecretKind,
    byte[] ProtectedSecret,
    string? Algorithm,
    string? Fingerprint,
    bool RequiresPassphrase,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt = null);

public enum SshCredentialSecretKind
{
    Password,
    PrivateKey
}
