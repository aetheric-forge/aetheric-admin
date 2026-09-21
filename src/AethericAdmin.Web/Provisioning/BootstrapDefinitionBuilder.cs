using System.Text.RegularExpressions;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// Translates a reviewed UniversityEnvelopeDraft into the four raw institution.yaml/
/// institution.bindings.yaml documents InstitutionBootstrapRequested actually needs.
///
/// Hard-codes the same topology runtime's own hand-authored fixtures already use
/// (institution/university.yaml, institution/campus.yaml): University owns Registry (Keycloak);
/// Campus owns Archive/Library/Post Office/Workbench and inherits Registry from University;
/// Faculty owns nothing and composes Decisions; Decisions inherits Archive/Library/Post
/// Office/Registrar from its ancestors and owns its own Workbench-backed Draft Workspace.
///
/// Decisions owning its Workbench (rather than inheriting an existing one, the way ADR Campus's
/// own pre-existing, populated Workbench does via runtime PR #37's parent-capability resolver)
/// is deliberate: this is always brand-new infrastructure with nothing to preserve, so minting a
/// fresh owned resource (Aetheric.Provisioning.Worker's Workbench provider construction, PR #36)
/// is the simpler, correct mechanism - the inherited-verification path exists specifically for
/// bringing already-populated infrastructure under provisioning without risking its data.
///
/// domains/organizations/roles/capabilities/workflows/policies are all left empty - runtime's
/// InstitutionYamlReader only requires resources/dependencies/initialState to be populated for
/// planning; the rest exist for a richer institutional model this bootstrap draft doesn't collect.
///
/// Resource-profile references (*ResourceProfile in the draft) are not interpreted - runtime has
/// no "resource profile" indirection layer yet; bucket/database/vhost/stage names are instead
/// derived deterministically from each institution's own operator-entered name.
/// </summary>
public static class BootstrapDefinitionBuilder
{
    // Must match the Worker's own Provisioning:WorkbenchTarget setting (default in both places:
    // "aetheric-provisioning-worker") - WorkbenchProvider requires the binding's own "target"
    // setting to equal whatever the constructing host supplies.
    public const string WorkbenchTarget = "aetheric-provisioning-worker";

    public static (InstitutionConfig University, InstitutionConfig Campus, InstitutionConfig Faculty, InstitutionConfig Decisions)
        Build(UniversityEnvelopeDraft draft)
    {
        var campusSlug = Slug(draft.Requests[1].Name);
        var decisionsSlug = Slug(draft.Requests[3].Name);
        return (University(draft.Identity.Realm), Campus(campusSlug), Faculty(), Decisions(decisionsSlug));
    }

    // Capped well under S3/Redis-stage length limits so every suffix ("-decisions-workspace" is
    // the longest, 21 characters) still fits comfortably within each provider's own constraints.
    private static string Slug(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) throw new ArgumentException("Name must contain at least one letter or digit.", nameof(name));
        return slug.Length > 30 ? slug[..30].TrimEnd('-') : slug;
    }

    private static InstitutionConfig University(string realm) => new(
        DefinitionYaml: """
        {
          "descriptor": {"id": "university", "name": "University", "version": "1.0.0", "description": "The root institution of the hierarchy."},
          "dependencies": [],
          "domains": [], "organizations": [], "roles": [], "capabilities": [],
          "resources": [
            {"id": "registry", "name": "Registry", "description": "The authoritative record of identities, roles, and credentials.", "type": "identity", "ownership": "owned"}
          ],
          "workflows": [], "policies": [],
          "initialState": {"configuration": {}}
        }
        """,
        BindingsYaml: $$"""
        {
          "institution": "university", "version": "1.0.0",
          "deployment": {"name": "production"},
          "bindings": {
            "registry": {"provider": "keycloak", "realm": "{{realm}}"}
          }
        }
        """);

    private static InstitutionConfig Campus(string slug) => new(
        DefinitionYaml: """
        {
          "descriptor": {"id": "campus", "name": "Campus", "version": "1.0.0", "description": "A Campus within the University."},
          "dependencies": [
            {"contract": "IRegistrar", "reason": "Authoritative identity shared across the University."}
          ],
          "domains": [], "organizations": [], "roles": [], "capabilities": [],
          "resources": [
            {"id": "archive", "name": "Archive", "description": "The Campus's constitutional record.", "type": "archive", "ownership": "owned"},
            {"id": "library", "name": "Library", "description": "The Campus's curated knowledge library.", "type": "knowledge", "ownership": "owned"},
            {"id": "post-office", "name": "Post Office", "description": "Message conveyance within the Campus.", "type": "post", "ownership": "owned"},
            {"id": "registry", "name": "Registry", "description": "Identity inherited from the University.", "type": "identity", "ownership": "parent"},
            {"id": "workbench", "name": "Workbench", "description": "Draft staging workspace.", "type": "staging", "ownership": "owned"}
          ],
          "workflows": [], "policies": [],
          "initialState": {"configuration": {}}
        }
        """,
        BindingsYaml: $$"""
        {
          "institution": "campus", "version": "1.0.0",
          "deployment": {"name": "production"},
          "bindings": {
            "archive": {"provider": "s3", "bucket": "{{slug}}-archive"},
            "library": {"provider": "mongodb", "database": "{{slug}}-library"},
            "post-office": {"provider": "rabbitmq", "vhost": "{{slug}}"},
            "IRegistrar": {"source": "university.registry"},
            "workbench": {"provider": "workbench", "backing": "redis", "target": "{{WorkbenchTarget}}", "stage": "{{slug}}-workbench", "fallback": "none"}
          }
        }
        """);

    // Not resource-backed: Faculty groups descendant institutions under a Dean, it owns no
    // infrastructure of its own (matching institution/campus.yaml's own note about Faculty).
    private static InstitutionConfig Faculty() => new(
        DefinitionYaml: """
        {
          "descriptor": {"id": "administration-faculty", "name": "Administration", "version": "1.0.0", "description": "Composes the Decisions Institution beneath the Campus."},
          "dependencies": [],
          "domains": [], "organizations": [], "roles": [], "capabilities": [],
          "resources": [],
          "workflows": [], "policies": [],
          "initialState": {"configuration": {}}
        }
        """,
        BindingsYaml: """
        {
          "institution": "administration-faculty", "version": "1.0.0",
          "deployment": {"name": "production"},
          "bindings": {}
        }
        """);

    private static InstitutionConfig Decisions(string slug) => new(
        DefinitionYaml: """
        {
          "descriptor": {"id": "decisions", "name": "Decisions", "version": "1.0.0", "description": "Drafts, proposes, and records the decisions made within its owning institution."},
          "dependencies": [
            {"contract": "IArchive", "reason": "Durable storage for decision records."},
            {"contract": "ILibrary", "reason": "Discovery surface for decision records."},
            {"contract": "IPostOffice", "reason": "Dispatch of decision-related messages."},
            {"contract": "IRegistrar", "reason": "Authoritative identity shared across the University."}
          ],
          "domains": [], "organizations": [], "roles": [], "capabilities": [],
          "resources": [
            {"id": "draft-workspace", "name": "Draft Workspace", "description": "In-progress decision drafts.", "type": "staging", "ownership": "owned"}
          ],
          "workflows": [], "policies": [],
          "initialState": {"configuration": {}}
        }
        """,
        BindingsYaml: $$"""
        {
          "institution": "decisions", "version": "1.0.0",
          "deployment": {"name": "production"},
          "bindings": {
            "IArchive": {"source": "campus.archive"},
            "ILibrary": {"source": "campus.library"},
            "IPostOffice": {"source": "campus.post-office"},
            "IRegistrar": {"source": "university.registry"},
            "draft-workspace": {"provider": "workbench", "backing": "redis", "target": "{{WorkbenchTarget}}", "stage": "{{slug}}-decisions-workspace", "fallback": "none"}
          }
        }
        """);
}
