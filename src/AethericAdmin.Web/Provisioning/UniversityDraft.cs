using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

namespace AethericAdmin.Web.Provisioning;

/// <summary>Admin-owned draft, not a runtime command. IDs survive edits and repeated reviews.</summary>
public sealed class UniversityDraft : IValidatableObject
{
    public Guid EnvelopeId { get; } = Guid.NewGuid();
    public Guid UniversityId { get; } = Guid.NewGuid();
    public Guid CampusId { get; } = Guid.NewGuid();
    public Guid FacultyId { get; } = Guid.NewGuid();
    public Guid DecisionsId { get; } = Guid.NewGuid();
    public Guid TalentId { get; } = Guid.NewGuid();

    [Required, StringLength(100), Display(Name = "University name")]
    public string UniversityName { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Campus name")]
    public string CampusName { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Faculty name")]
    public string FacultyName { get; set; } = "Administration";
    [Required, StringLength(100), Display(Name = "Decisions name")]
    public string DecisionsName { get; set; } = "Decisions";
    public bool IncludeTalent { get; set; }
    public string TalentName { get; set; } = "Talent";

    [Required, StringLength(253), Display(Name = "Keycloak authority")]
    public string KeycloakAuthority { get; set; } = "";
    [Required, StringLength(200), Display(Name = "Keycloak realm")]
    public string KeycloakRealm { get; set; } = "";
    [Required, StringLength(200), Display(Name = "Keycloak client ID")]
    public string KeycloakClientId { get; set; } = "";
    [Required, StringLength(200), Display(Name = "Root administrator subject ID")]
    public string RootAdministratorSubjectId { get; set; } = "";

    // These are definition references, not invented resource bundles or provider settings.
    [StringLength(200)]
    public string UniversityResourceProfile { get; set; } = "";
    [StringLength(200)]
    public string CampusResourceProfile { get; set; } = "";
    [StringLength(200)]
    public string FacultyResourceProfile { get; set; } = "";
    [StringLength(200)]
    public string IamDefinitionReference { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IncludeTalent && (string.IsNullOrWhiteSpace(TalentName) || TalentName.Length > 100))
            yield return new("Enter a Talent institution name of at most 100 characters.", [nameof(TalentName)]);
        if (IncludeTalent && string.Equals(TalentName.Trim(), DecisionsName.Trim(), StringComparison.OrdinalIgnoreCase))
            yield return new("Give Decisions and Talent distinct names within the Faculty.", [nameof(TalentName)]);
        if (!Uri.TryCreate(KeycloakAuthority, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            yield return new("Enter an HTTPS Keycloak authority without credentials, a query, or a fragment.", [nameof(KeycloakAuthority)]);
    }

    public UniversityEnvelopeDraft Review()
    {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);
        var requests = ImmutableArray.CreateBuilder<UniversityRequestDraft>();
        requests.Add(new(UniversityId, "University", UniversityName.Trim(), null, null, [], Profile(UniversityResourceProfile)));
        requests.Add(new(CampusId, "Campus", CampusName.Trim(), UniversityId, UniversityId, [UniversityId], Profile(CampusResourceProfile)));
        requests.Add(new(FacultyId, "Faculty", FacultyName.Trim(), CampusId, UniversityId, [CampusId], Profile(FacultyResourceProfile)));
        requests.Add(new(DecisionsId, "Decisions", DecisionsName.Trim(), FacultyId, UniversityId, [FacultyId], null));
        if (IncludeTalent)
            requests.Add(new(TalentId, "Talent", TalentName.Trim(), FacultyId, UniversityId, [FacultyId], null));

        return new("aetheric-admin/university-draft/v1", EnvelopeId, UniversityId, "Standard",
            new(KeycloakAuthority.Trim().TrimEnd('/'), KeycloakRealm.Trim(), KeycloakClientId.Trim(),
                RootAdministratorSubjectId.Trim(), Profile(IamDefinitionReference)),
            [new("redis", "redis"), new("rabbitmq", "rabbitmq"), new("postgres", "postgres"), new("mongo", "mongo")],
            requests.ToImmutable());
    }

    private static string? Profile(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record UniversityEnvelopeDraft(
    string Schema, Guid EnvelopeId, Guid InitiatingRequestId, string Priority,
    UniversityIdentityDraft Identity, ImmutableArray<RootCredentialReference> RootCredentialReferences,
    ImmutableArray<UniversityRequestDraft> Requests);

public sealed record UniversityIdentityDraft(string Authority, string Realm, string ClientId,
    string RootAdministratorSubjectId, string? IamDefinitionReference);

// Store keys identify existing encrypted bootstrap credentials; secret values never enter this draft.
public sealed record RootCredentialReference(string System, string StoreKey);

public sealed record UniversityRequestDraft(Guid RequestId, string Kind, string Name,
    Guid? ParentRequestId, Guid? CausedByRequestId, ImmutableArray<Guid> DependsOn,
    string? PrivateResourceProfileReference);

/// <summary>Lives for the setup's interactive circuit; never shared between operators.</summary>
public sealed class UniversityDraftSession
{
    public UniversityDraft Draft { get; } = new();
}
