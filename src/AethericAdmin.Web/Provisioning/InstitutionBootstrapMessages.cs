using System.Collections.Immutable;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Models.Post;

namespace AethericAdmin.Web.Provisioning;

/// <summary>Runtime bootstrap v1 wire contract. Keep compatible with runtime's canonical JSON fixtures.
/// This is not UniversityEnvelopeDraft and is not populated until definitions and credentials are resolved.</summary>
public sealed record InstitutionBootstrapRequested(
    Guid RequestId,
    InstitutionConfig University,
    InstitutionConfig Campus,
    InstitutionConfig AdministrationFaculty,
    InstitutionConfig Decisions,
    IReadOnlyDictionary<string, BootstrapRootCredentialPayload> RootCredentials,
    DateTimeOffset RequestedAtUtc);

/// <summary>
/// Raw institution.yaml/institution.bindings.yaml-shaped text, submitted inline - the form IS the
/// content, not a GitHub commit reference. Turned into a SourceBundle with synthetic,
/// obviously-non-GitHub provenance by InlineSourceDocuments.Build.
/// </summary>
public sealed record InstitutionConfig(string DefinitionYaml, string BindingsYaml);

public enum BootstrapStepStatus { Succeeded, Failed, NotAttempted }

public sealed record BootstrapStepResult(string Step, BootstrapStepStatus Status, IReadOnlyList<string> Issues);

public sealed record InstitutionBootstrapCompleted(
    Guid RequestId,
    bool Succeeded,
    ImmutableArray<BootstrapStepResult> Steps,
    DateTimeOffset CompletedAtUtc);

public static class ProvisioningBootstrapPost
{
    public static IPostReference RequestReference() => new PostReference(
        ProvisioningPost.Domain,
        "institution/deploy/bootstrap",
        new PostContract("institution-bootstrap-requested", "1.0", PostIntent.Command));

    public static IPostReference ResultReference() => new PostReference(
        ProvisioningPost.Domain,
        "institution/deploy/bootstrap/result",
        new PostContract("institution-bootstrap-completed", "1.0", PostIntent.Event));
}

// Separate from the legacy Campus payload, whose wire contract is being retired separately.
public sealed record BootstrapRootCredentialPayload(
    string Host, int Port, string? Username, string Password,
    string? AuthDatabase = null, string? Database = null,
    string? Scheme = null, string? BasePath = null, string? Realm = null);
