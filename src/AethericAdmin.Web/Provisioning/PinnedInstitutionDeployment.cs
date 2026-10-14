using System.Collections.Concurrent;
using Aetheric.Provisioning.Engine;
using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// An institution an operator can deploy as-is: an exact reviewed Git revision plus the root
/// systems its bindings provision. Only those systems' credentials leave the encrypted store.
/// </summary>
public sealed record PinnedInstitution(
    string Id,
    string Name,
    string Repository,
    string Revision,
    string DefinitionPath,
    string BindingsPath,
    IReadOnlyList<string> RequiredSystems);

public static class PinnedInstitutions
{
    // Must match aetheric-web's institution-modules.json pin: the web shell only accepts a
    // consumer artifact whose deployment identity matches the module it loaded.
    public static IReadOnlyList<PinnedInstitution> All { get; } =
    [
        new("library", "Library", "https://github.com/aetheric-forge/library",
            "e09433f55ad82bec2416c4f10ec14ee6f78a7493",
            "institution/library.yaml", "institution/library.bindings.yaml", ["mongo"]),
    ];

    public static PinnedInstitution? Find(string id) => All.FirstOrDefault(x => x.Id == id);
}

/// <summary>Publishes an institution/deploy request for a pinned institution.</summary>
public sealed class PinnedInstitutionDeployment(IRootCredentialStore credentialStore, IPostService postService)
{
    public sealed class MissingCredentialException(string system)
        : Exception($"No root credential is stored for '{system}'. Add it through infrastructure bootstrap first.");

    public async Task<Guid> SubmitAsync(PinnedInstitution institution, CancellationToken ct)
    {
        var credentials = new Dictionary<string, BootstrapRootCredentialPayload>(StringComparer.Ordinal);
        foreach (var system in institution.RequiredSystems)
        {
            var credential = await credentialStore.TryReadAsync(system, ct)
                ?? throw new MissingCredentialException(system);
            credentials[system] = ToPayload(credential);
        }

        var request = new InstitutionDeploymentRequested(
            Guid.NewGuid(),
            institution.Repository,
            institution.Revision,
            institution.DefinitionPath,
            institution.BindingsPath,
            credentials,
            Parent: null,
            DateTimeOffset.UtcNow);

        await postService.PublishAsync(InstitutionDeploymentPost.RequestReference(), request, ct: ct);
        return request.RequestId;
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

/// <summary>
/// Most recent InstitutionDeploymentCompleted per request, in process memory only - mirrors
/// BootstrapResultStore. The worker's catalog remains the durable record.
/// </summary>
public sealed class InstitutionDeploymentResultStore
{
    private readonly ConcurrentDictionary<Guid, InstitutionDeploymentCompleted> _results = new();

    public void Record(InstitutionDeploymentCompleted result) => _results[result.RequestId] = result;

    public InstitutionDeploymentCompleted? TryGet(Guid requestId) =>
        _results.TryGetValue(requestId, out var result) ? result : null;
}

public sealed class InstitutionDeploymentResultConsumer(InstitutionDeploymentResultStore store)
    : MessageConsumerBase<InstitutionDeploymentCompleted>
{
    public override IPostContract Contract => InstitutionDeploymentPost.ResultReference().Contract;

    public override Task ConsumeAsync(
        InstitutionDeploymentCompleted message,
        IPostContext context,
        CancellationToken ct = default)
    {
        store.Record(message);
        return Task.CompletedTask;
    }
}
