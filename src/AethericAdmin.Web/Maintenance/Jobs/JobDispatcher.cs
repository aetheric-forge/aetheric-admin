using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;

namespace AethericAdmin.Web.Maintenance.Jobs;

/// <summary>
/// The actual post-collect-execute-record cycle for a JobDefinition, shared between the periodic
/// polling loop (MaintenanceDispatchService) and the admin UI's ad-hoc "Run now" button, so there's
/// one place that owns what "running a job" means.
/// </summary>
public sealed class JobDispatcher(
    IEnumerable<IJobExecutor> executors,
    TimeProvider timeProvider,
    ILogger<JobDispatcher> logger)
{
    public const string Domain = "aetheric-web";

    private readonly IReadOnlyDictionary<JobDefinitionKind, IJobExecutor> _executorsByKind =
        executors.ToDictionary(executor => executor.Kind);

    public async Task RunAsync(
        ICaretaker caretaker,
        JobDefinition job,
        string? passphrase,
        CancellationToken ct)
    {
        if (!_executorsByKind.TryGetValue(job.Kind, out var executor))
        {
            logger.LogWarning("No executor registered for job kind {Kind} (job {Job}).", job.Kind, job.Name);
            return;
        }

        try
        {
            var command = new MaintenanceCommand(
                Guid.NewGuid(),
                Domain,
                job.Id.ToString(),
                timeProvider.GetUtcNow(),
                Source: nameof(JobDispatcher));
            await caretaker.PostAsync(Domain, command, ct).ConfigureAwait(false);

            var collected = await caretaker.CollectNextAsync(Domain, job.Id.ToString(), ct).ConfigureAwait(false);
            if (collected is null)
            {
                return;
            }

            var outcome = await executor.RunAsync(job, collected, passphrase, ct).ConfigureAwait(false);
            await caretaker.RecordOutcomeAsync(Domain, outcome, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Dispatching job definition {Job} failed.", job.Name);
        }
    }
}
