using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Models.Post;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// Sent admin -> provisioner: run the plan/execute pipeline for the named institution
/// definition, using the already-bootstrapped root credentials named by
/// InfrastructureConnections.Systems (redis/rabbitmq/postgres/mongo). Credentials travel
/// inline rather than by reference - the RabbitMQ broker connection is already the
/// TLS-secured channel this same data crosses during bootstrap credential testing.
/// </summary>
public sealed record CampusDeploymentRequested(
    Guid RequestId,
    string Repository,
    string Revision,
    string DefinitionPath,
    string BindingsPath,
    IReadOnlyDictionary<string, RootCredentialPayload> RootCredentials,
    DateTimeOffset RequestedAtUtc);

public sealed record RootCredentialPayload(
    string Host,
    int Port,
    string? Username,
    string Password,
    string? AuthDatabase = null,
    string? Database = null,
    string? Scheme = null,
    string? BasePath = null);

/// <summary>
/// Sent provisioner -> admin: the outcome of the requested plan/execute run. Today this is
/// expected to report failure (provider.unsupported for every resource except Workbench) -
/// that's the correct, checked-in behavior until real resource providers exist.
/// </summary>
public sealed record CampusDeploymentCompleted(
    Guid RequestId,
    bool Succeeded,
    IReadOnlyList<string> Issues,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// The Post references and contract this message pair travels on. Defined independently in
/// aetheric-admin and aetheric-provisioning per Post's JSON-structural wire format - the two
/// sides only need to agree on Domain/Address/Contract.Name/Contract.Version, not share a
/// compiled type.
/// </summary>
public static class ProvisioningPost
{
    public const string Domain = "provisioning";

    public static IPostReference RequestReference() => new PostReference(
        Domain,
        "campus/deploy",
        new PostContract("campus-deployment-requested", "1.0", PostIntent.Command));

    public static IPostReference ResultReference() => new PostReference(
        Domain,
        "campus/deploy/result",
        new PostContract("campus-deployment-completed", "1.0", PostIntent.Event));
}
