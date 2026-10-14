using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Models.Post;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// Runtime's institution/deploy 2.0 wire contract (Aetheric.Provisioning.Worker's
/// InstitutionDeploymentRequested), defined independently here per Post's JSON-structural
/// format. Replaces the retired campus/deploy 1.0 shape the legacy endpoint still sends. The
/// worker loads the pinned definition from Git, runs configure/review/approve/execute, and for
/// Library/Archive/Decisions delivers the scoped consumer configuration to its web Secret.
/// </summary>
public sealed record InstitutionDeploymentRequested(
    Guid RequestId,
    string Repository,
    string Revision,
    string DefinitionPath,
    string BindingsPath,
    IReadOnlyDictionary<string, BootstrapRootCredentialPayload> RootCredentials,
    InstitutionParentIdentity? Parent,
    DateTimeOffset RequestedAtUtc);

/// <summary>Null for a root institution such as Library, whose definition has no dependencies.</summary>
public sealed record InstitutionParentIdentity(
    string Repository,
    string Revision,
    IReadOnlyDictionary<string, string> ResourceLocations);

public sealed record InstitutionDeploymentCompleted(
    Guid RequestId,
    bool Succeeded,
    IReadOnlyList<string> Issues,
    DateTimeOffset CompletedAtUtc);

public static class InstitutionDeploymentPost
{
    public static IPostReference RequestReference() => new PostReference(
        ProvisioningPost.Domain,
        "institution/deploy",
        new PostContract("institution-deployment-requested", "2.0", PostIntent.Command));

    public static IPostReference ResultReference() => new PostReference(
        ProvisioningPost.Domain,
        "institution/deploy/result",
        new PostContract("institution-deployment-completed", "2.0", PostIntent.Event));
}
