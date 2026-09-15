using AethericContracts.Membership;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;

namespace AethericAdmin.Web.Maintenance;

/// <summary>
/// Flags membership applications that have sat unreviewed too long. Read-only - it reports on stale
/// applications (via warning-level logs, one per application) rather than mutating them, so it's safe
/// to run repeatedly without side effects on the applications themselves.
/// </summary>
public sealed class StaleMembershipApplicationsWorker(
    IMembershipApplicationStore store,
    TimeProvider timeProvider,
    ILogger<StaleMembershipApplicationsWorker> logger) : IMaintenanceWorker
{
    public const string JobName = "flag-stale-membership-applications";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(14);

    public string Job => JobName;

    public async Task<MaintenanceRunOutcome> RunAsync(
        MaintenanceCommand command,
        CancellationToken ct = default)
    {
        var applications = await store.ListAsync(ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        var stale = applications
            .Where(application => application.Status is
                MembershipApplicationStatus.Submitted or MembershipApplicationStatus.UnderReview)
            .Where(application => now - application.SubmittedAt >= StaleAfter)
            .ToArray();

        foreach (var application in stale)
        {
            logger.LogWarning(
                "Membership application {ApplicationId} for {Email} has been pending review for {Days} days.",
                application.Id,
                application.Email,
                (now - application.SubmittedAt).Days);
        }

        return new MaintenanceRunOutcome(
            command.Id,
            MaintenanceRunStatus.Completed,
            ProcessedCount: stale.Length,
            RemainingCount: 0,
            OccurredAtUtc: now);
    }
}
