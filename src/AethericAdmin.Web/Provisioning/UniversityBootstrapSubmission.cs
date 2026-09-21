using Aetheric.Provisioning.Engine;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// Translates a reviewed UniversityEnvelopeDraft into a real InstitutionBootstrapRequested and
/// publishes it - the piece UniversitySetup.razor's "Submit to Operations" button was waiting on.
///
/// Resolves exactly the systems Aetheric.Provisioning.Worker's BuildProviders actually consumes
/// for institution provisioning - rabbitmq, mongo, keycloak, s3, and redis (for the Workbench
/// resources BootstrapDefinitionBuilder now generates) - via the same IRootCredentialStore.
/// TryReadAsync pattern CampusDeploymentEndpoints.cs already established for the legacy endpoint.
/// Deliberately excludes postgres: the Worker never reads it for institution provisioning.
/// </summary>
public sealed class UniversityBootstrapSubmission(IRootCredentialStore credentialStore, IPostService postService)
{
    private static readonly string[] RequiredSystems = ["rabbitmq", "mongo", "keycloak", "s3", "redis"];

    public sealed class MissingCredentialException(string system)
        : Exception($"No root credential is stored for '{system}'. Add it before submitting a University bootstrap.");

    public async Task<Guid> SubmitAsync(UniversityEnvelopeDraft draft, CancellationToken ct)
    {
        var requestedAtUtc = DateTimeOffset.UtcNow;
        var metadata = BootstrapDraftIdentity.Create(draft, requestedAtUtc);
        var (university, campus, faculty, decisions) = BootstrapDefinitionBuilder.Build(draft);
        var credentials = await ResolveCredentialsAsync(ct);

        var request = new InstitutionBootstrapRequested(
            draft.EnvelopeId, university, campus, faculty, decisions, credentials, requestedAtUtc);

        await postService.PublishAsync(ProvisioningBootstrapPost.RequestReference(), request, metadata, ct);
        return draft.EnvelopeId;
    }

    private async Task<Dictionary<string, BootstrapRootCredentialPayload>> ResolveCredentialsAsync(CancellationToken ct)
    {
        var credentials = new Dictionary<string, BootstrapRootCredentialPayload>(StringComparer.Ordinal);
        foreach (var system in RequiredSystems)
        {
            var credential = await credentialStore.TryReadAsync(system, ct)
                ?? throw new MissingCredentialException(system);
            credentials[system] = ToPayload(credential);
        }
        return credentials;
    }

    private static BootstrapRootCredentialPayload ToPayload(RootCredential credential) => new(
        Host: credential.Host,
        Port: credential.Port,
        Username: credential.Username,
        Password: credential.Password,
        AuthDatabase: credential.Mongo?.AuthDatabase,
        Database: credential.Postgres?.Database,
        Scheme: credential.RabbitMq?.Scheme ?? credential.Keycloak?.Scheme ?? credential.S3?.Scheme,
        BasePath: credential.RabbitMq?.BasePath ?? credential.Keycloak?.BasePath,
        Realm: credential.Keycloak?.Realm);
}
