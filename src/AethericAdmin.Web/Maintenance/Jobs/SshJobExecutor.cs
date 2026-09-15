using System.Text;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;
using Renci.SshNet;

namespace AethericAdmin.Web.Maintenance.Jobs;

/// <summary>
/// Connects using the job's stored SshCredential (never touching the raw secret outside
/// ICredentialStore.UseSecretAsync's callback) and runs its configured command. SSH.NET's
/// Connect/Execute calls are synchronous with no true cancellation support - Task.Run only keeps the
/// caller responsive, it doesn't interrupt an in-flight SSH operation, hence the connection timeout
/// below as the actual bound on a stuck connection.
/// </summary>
public sealed class SshJobExecutor(ICredentialStore credentialStore) : IJobExecutor
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public JobDefinitionKind Kind => JobDefinitionKind.Ssh;

    public async Task<MaintenanceRunOutcome> RunAsync(
        JobDefinition job,
        MaintenanceCommand command,
        string? passphrase,
        CancellationToken ct)
    {
        var config = job.Ssh ?? throw new InvalidOperationException($"Job '{job.Name}' has no SSH configuration.");
        var credential = await credentialStore.GetAsync(config.CredentialId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"SSH credential {config.CredentialId} was not found.");

        try
        {
            var (exitStatus, error) = await credentialStore.UseSecretAsync(
                credential.Id,
                (secret, innerCt) => RunCommandAsync(credential, config, secret, passphrase, innerCt),
                ct).ConfigureAwait(false);

            var status = exitStatus == 0 ? MaintenanceRunStatus.Completed : MaintenanceRunStatus.Failed;
            return new MaintenanceRunOutcome(
                command.Id,
                status,
                ProcessedCount: 1,
                RemainingCount: 0,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                FailureReason: status == MaintenanceRunStatus.Failed ? $"Exit code {exitStatus}: {Truncate(error)}" : null);
        }
        catch (Exception exception)
        {
            return new MaintenanceRunOutcome(
                command.Id,
                MaintenanceRunStatus.Failed,
                ProcessedCount: 0,
                RemainingCount: 0,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                FailureReason: Truncate(exception.Message));
        }
    }

    private static Task<(int ExitStatus, string Error)> RunCommandAsync(
        SshCredential credential,
        SshJobConfig config,
        string secret,
        string? passphrase,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var connectionInfo = BuildConnectionInfo(credential, config, secret, passphrase);
            using var client = new SshClient(connectionInfo);
            client.Connect();
            try
            {
                using var sshCommand = client.CreateCommand(config.Command);
                sshCommand.Execute();
                return (sshCommand.ExitStatus ?? -1, sshCommand.Error);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    private static Renci.SshNet.ConnectionInfo BuildConnectionInfo(
        SshCredential credential,
        SshJobConfig config,
        string secret,
        string? passphrase)
    {
        if (credential.SecretKind == SshCredentialSecretKind.Password)
        {
            return new PasswordConnectionInfo(config.Host, config.Port, credential.Username, secret)
            {
                Timeout = ConnectTimeout
            };
        }

        using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(secret));
        var keyFile = credential.RequiresPassphrase
            ? new PrivateKeyFile(keyStream, passphrase)
            : new PrivateKeyFile(keyStream);

        return new PrivateKeyConnectionInfo(config.Host, config.Port, credential.Username, keyFile)
        {
            Timeout = ConnectTimeout
        };
    }

    private static string Truncate(string? value, int max = 500) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max] + "…";
}
