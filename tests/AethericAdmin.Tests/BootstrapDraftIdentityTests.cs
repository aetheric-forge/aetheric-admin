using AethericAdmin.Web.Provisioning;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class BootstrapDraftIdentityTests
{
    private static UniversityEnvelopeDraft Draft() => new UniversityDraft
    {
        UniversityName = "University", CampusName = "Campus",
        KeycloakAuthority = "https://identity.example", KeycloakRealm = "forge",
        KeycloakClientId = "admin", RootAdministratorSubjectId = "subject"
    }.Review();

    [Fact]
    public void Actual_draft_maps_envelope_and_all_four_request_ids_without_requiring_secrets()
    {
        var draft = Draft();
        var timestamp = DateTimeOffset.UtcNow;
        var metadata = BootstrapDraftIdentity.Create(draft, timestamp);
        Assert.Equal(draft.EnvelopeId.ToString("D"), metadata.MessageId);
        Assert.Equal(draft.InitiatingRequestId.ToString("D"), metadata.CorrelationId);
        Assert.Null(metadata.CausationId);
        Assert.Equal(timestamp, metadata.ProducedAtUtc);
        Assert.Equal(draft.Priority, metadata.Attributes[BootstrapPostMetadata.Priority]);
        string[] keys = [BootstrapPostMetadata.UniversityRequestId, BootstrapPostMetadata.CampusRequestId,
            BootstrapPostMetadata.FacultyRequestId, BootstrapPostMetadata.DecisionsRequestId];
        for (var i = 0; i < keys.Length; i++)
            Assert.Equal(draft.Requests[i].RequestId.ToString("D"), metadata.Attributes[keys[i]]);
    }

    [Fact]
    public void Unsupported_schema_priority_extra_institutions_and_identity_graphs_are_rejected()
    {
        var draft = Draft();
        var decisions = draft.Requests[3];
        UniversityEnvelopeDraft[] invalid = [
            draft with { Schema = "unknown" }, draft with { Priority = "Urgent" },
            draft with { InitiatingRequestId = Guid.NewGuid() },
            draft with { Requests = draft.Requests.Add(decisions with { Kind = "Talent", RequestId = Guid.NewGuid() }) },
            draft with { Requests = draft.Requests.SetItem(3, decisions with { ParentRequestId = draft.InitiatingRequestId }) },
            draft with { Requests = draft.Requests.SetItem(3, decisions with { CausedByRequestId = draft.Requests[2].RequestId }) },
            draft with { Requests = draft.Requests.SetItem(3, decisions with { DependsOn = [] }) },
            draft with { EnvelopeId = draft.InitiatingRequestId },
            draft with { Requests = draft.Requests.SetItem(3, decisions with { RequestId = Guid.Empty }) }
        ];
        foreach (var item in invalid)
            Assert.Throws<ArgumentException>(() => BootstrapDraftIdentity.Create(item, DateTimeOffset.UtcNow));
    }
}
