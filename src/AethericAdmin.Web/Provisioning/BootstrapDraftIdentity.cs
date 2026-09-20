using AethericForge.Runtime.Models.Post;

namespace AethericAdmin.Web.Provisioning;

/// <summary>Maps only a draft's supported identity graph. Does not resolve configuration,
/// retrieve credentials, approve a deployment, or publish a message.</summary>
public static class BootstrapDraftIdentity
{
    public static PostMetadata Create(UniversityEnvelopeDraft draft, DateTimeOffset requestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Schema != "aetheric-admin/university-draft/v1" || draft.Priority != "Standard")
            throw new ArgumentException("Unsupported draft schema or priority.", nameof(draft));
        string[] kinds = ["University", "Campus", "Faculty", "Decisions"];
        if (draft.Requests.IsDefault || draft.Requests.Length != kinds.Length || draft.Requests.Any(request => request is null))
            throw new ArgumentException("Bootstrap requires exactly University, Campus, Faculty, and Decisions; Talent is unsupported.", nameof(draft));
        for (var i = 0; i < kinds.Length; i++)
        {
            var request = draft.Requests[i];
            var parent = i == 0 ? (Guid?)null : draft.Requests[i - 1].RequestId;
            var cause = i == 0 ? (Guid?)null : draft.InitiatingRequestId;
            if (request.Kind != kinds[i] || request.ParentRequestId != parent || request.CausedByRequestId != cause
                || request.DependsOn.IsDefault || (i == 0 ? !request.DependsOn.IsEmpty
                    : request.DependsOn.Length != 1 || request.DependsOn[0] != parent))
                throw new ArgumentException("The draft does not match the fixed bootstrap containment and causation graph.", nameof(draft));
        }
        if (draft.InitiatingRequestId != draft.Requests[0].RequestId)
            throw new ArgumentException("University must initiate the bootstrap.", nameof(draft));
        return BootstrapPostMetadata.CreateRequest(draft.EnvelopeId, draft.Requests[0].RequestId,
            draft.Requests[1].RequestId, draft.Requests[2].RequestId, draft.Requests[3].RequestId, requestedAtUtc);
    }
}
