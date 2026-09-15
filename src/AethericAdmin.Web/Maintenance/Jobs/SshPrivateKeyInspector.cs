using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Security;
using Org.BouncyCastle.Crypto;

namespace AethericAdmin.Web.Maintenance.Jobs;

/// <summary>
/// Validates a pasted private key and derives an algorithm + SHA256 fingerprint for display, without
/// ever persisting or returning the raw key. Ported from aetheric-gm's
/// AethericGm.Infrastructure.Profiles.SshPrivateKeyInspector (also SSH.NET-based) rather than
/// reinventing key validation.
/// </summary>
internal static class SshPrivateKeyInspector
{
    public static SshPrivateKeyInspection Inspect(string privateKey, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new SshCredentialValidationException("Private key is required.");
        }

        if (privateKey.Length > 128 * 1024)
        {
            throw new SshCredentialValidationException("Private key exceeds the 128 KB limit.");
        }

        var bytes = Encoding.UTF8.GetBytes(privateKey.Trim());
        try
        {
            if (TryOpen(bytes, null, out var inspection))
            {
                if (!string.IsNullOrEmpty(passphrase))
                {
                    throw new SshCredentialValidationException(
                        "The pasted key opened without a passphrase, so the supplied passphrase was not " +
                        "validated. Import cancelled. Leave the passphrase blank only if this is the key " +
                        "you intend to import.");
                }

                return inspection with { RequiresPassphrase = false };
            }

            if (string.IsNullOrEmpty(passphrase))
            {
                throw new SshCredentialValidationException("The private key is invalid or requires a passphrase.");
            }

            if (TryOpen(bytes, passphrase, out inspection))
            {
                return inspection with { RequiresPassphrase = true };
            }

            throw new SshCredentialValidationException("The private key or passphrase is invalid, or the key format is unsupported.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool TryOpen(byte[] privateKey, string? passphrase, out SshPrivateKeyInspection inspection)
    {
        try
        {
            using var stream = new MemoryStream(privateKey, writable: false);
            using var keyFile = passphrase is null ? new PrivateKeyFile(stream) : new PrivateKeyFile(stream, passphrase);
            var algorithm = keyFile.Key.ToString() ?? throw new SshException("Private key algorithm is unavailable.");
            var publicData = keyFile.HostKeyAlgorithms.OfType<KeyHostAlgorithm>().First().Data;
            var digest = SHA256.HashData(publicData);
            try
            {
                inspection = new SshPrivateKeyInspection(algorithm, $"SHA256:{Convert.ToBase64String(digest).TrimEnd('=')}", false);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
        catch (Exception exception) when (exception is SshException or NotSupportedException or ArgumentException
            or FormatException or CryptographicException or InvalidCipherTextException)
        {
            inspection = default;
            return false;
        }
    }
}

internal readonly record struct SshPrivateKeyInspection(string Algorithm, string Fingerprint, bool RequiresPassphrase);

public sealed class SshCredentialValidationException(string message) : Exception(message);
