using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Models.Post;

namespace AethericAdmin.Web.Provisioning;

/// <summary>Supplemental identity headers for the fixed four-institution bootstrap v1 contract.
/// Payload RequestId and Post MessageId identify the envelope; correlation identifies University.
/// Child containment and causation are defined by the fixed topology, not by delivery order.</summary>
public static class BootstrapPostMetadata
{
    public const string Priority = "bootstrap.priority";
    public const string UniversityRequestId = "bootstrap.university-request-id";
    public const string CampusRequestId = "bootstrap.campus-request-id";
    public const string FacultyRequestId = "bootstrap.administration-faculty-request-id";
    public const string DecisionsRequestId = "bootstrap.decisions-request-id";

    public static PostMetadata CreateRequest(Guid envelopeId, Guid universityId, Guid campusId,
        Guid facultyId, Guid decisionsId, DateTimeOffset requestedAtUtc)
    {
        var ids = new[] { envelopeId, universityId, campusId, facultyId, decisionsId };
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Envelope and institution request IDs must be nonempty and distinct.");
        return new PostMetadata(messageId: envelopeId.ToString("D"),
            correlationId: universityId.ToString("D"), producedAtUtc: requestedAtUtc,
            attributes: new Dictionary<string, string>
            {
                [Priority] = "Standard",
                [UniversityRequestId] = universityId.ToString("D"),
                [CampusRequestId] = campusId.ToString("D"),
                [FacultyRequestId] = facultyId.ToString("D"),
                [DecisionsRequestId] = decisionsId.ToString("D")
            });
    }

    // Allowlist identity headers: do not reflect arbitrary incoming attributes into results.
    // Legacy v1 producers without these supplemental headers remain supported.
    public static PostMetadata CreateCompletion(IPostMetadata request, string? messageId = null,
        DateTimeOffset? completedAtUtc = null) => new(
            messageId: messageId,
            correlationId: request.CorrelationId ?? request.MessageId,
            causationId: request.MessageId,
            producedAtUtc: completedAtUtc,
            attributes: request.Attributes.Where(pair => pair.Key is Priority or UniversityRequestId
                or CampusRequestId or FacultyRequestId or DecisionsRequestId)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
}
