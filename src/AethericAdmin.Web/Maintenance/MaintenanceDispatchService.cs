using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using AethericForge.Runtime.Institutions.Campus;
using AethericForge.Runtime.Institutions.Maintenance;
using AethericAdmin.Web.Hosting;
using AethericAdmin.Web.Maintenance.Jobs;
using Cronos;

namespace AethericAdmin.Web.Maintenance;

/// <summary>
/// Two things share this single poll loop: the original fixed-interval DI-registered
/// IMaintenanceWorker path (unchanged - still runs every 24h), and admin-defined JobDefinitions on
/// their own cron schedules (polled every minute, run when due per Cronos). Both go through the same
/// Caretaker post/collect/record-outcome cycle; JobDefinition dispatch itself lives in JobDispatcher
/// so the admin UI's ad-hoc "Run now" button can reuse the exact same code path outside this loop.
/// </summary>
public sealed class MaintenanceDispatchService(
    ICampus campus,
    IEnumerable<IMaintenanceWorker> workers,
    IJobDefinitionStore jobDefinitionStore,
    JobDispatcher jobDispatcher,
    TimeProvider timeProvider,
    ILogger<MaintenanceDispatchService> logger) : BackgroundService
{
    private const string Domain = "aetheric-admin";
    private static readonly TimeSpan LegacyWorkerInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workersByJob = workers.ToDictionary(worker => worker.Job, StringComparer.Ordinal);
        var caretaker = campus.Resolve<IOperationsFaculty>().Resolve<IMaintenance>().Caretaker;
        var legacyNextRun = workersByJob.Keys.ToDictionary(job => job, _ => timeProvider.GetUtcNow());

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();

            foreach (var (job, worker) in workersByJob)
            {
                if (now < legacyNextRun[job])
                {
                    continue;
                }

                await RunLegacyWorkerAsync(caretaker, job, worker, stoppingToken).ConfigureAwait(false);
                legacyNextRun[job] = now + LegacyWorkerInterval;
            }

            await DispatchDueJobDefinitionsAsync(caretaker, now, stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchDueJobDefinitionsAsync(ICaretaker caretaker, DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<MaintenanceRunRecord> runs;
        IReadOnlyList<JobDefinition> definitions;
        try
        {
            runs = await caretaker.ListRunsAsync(JobDispatcher.Domain, ct).ConfigureAwait(false);
            definitions = await jobDefinitionStore.ListEnabledAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to load job definitions or run history for scheduling.");
            return;
        }

        foreach (var definition in definitions)
        {
            if (definition.CronSchedule is null)
            {
                continue; // ad-hoc only - never fires from the poll loop
            }

            CronExpression schedule;
            try
            {
                schedule = CronExpression.Parse(definition.CronSchedule);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Job {Job} has an invalid cron schedule '{Schedule}'.", definition.Name, definition.CronSchedule);
                continue;
            }

            var lastRun = runs
                .Where(record => record.Command.Job == definition.Id.ToString())
                .Select(record => record.Outcome?.OccurredAtUtc ?? record.Command.RequestedAtUtc)
                .DefaultIfEmpty()
                .Max();

            var fromUtc = (lastRun == default ? now.AddDays(-1) : lastRun).UtcDateTime;
            var next = schedule.GetNextOccurrence(fromUtc, TimeZoneInfo.Utc);
            if (next is not null && next.Value <= now.UtcDateTime)
            {
                await jobDispatcher.RunAsync(caretaker, definition, passphrase: null, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RunLegacyWorkerAsync(
        ICaretaker caretaker,
        string job,
        IMaintenanceWorker worker,
        CancellationToken ct)
    {
        try
        {
            var command = new MaintenanceCommand(
                Guid.NewGuid(),
                Domain,
                job,
                timeProvider.GetUtcNow(),
                Source: nameof(MaintenanceDispatchService));
            await caretaker.PostAsync(Domain, command, ct).ConfigureAwait(false);

            var collected = await caretaker.CollectNextAsync(Domain, job, ct).ConfigureAwait(false);
            if (collected is null)
            {
                return;
            }

            var outcome = await worker.RunAsync(collected, ct).ConfigureAwait(false);
            await caretaker.RecordOutcomeAsync(Domain, outcome, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A maintenance dispatch iteration failed for job {Job}.", job);
        }
    }
}
