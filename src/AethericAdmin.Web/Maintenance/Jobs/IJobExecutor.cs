using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;

namespace AethericAdmin.Web.Maintenance.Jobs;

public interface IJobExecutor
{
    JobDefinitionKind Kind { get; }

    Task<MaintenanceRunOutcome> RunAsync(
        JobDefinition job,
        MaintenanceCommand command,
        string? passphrase,
        CancellationToken ct);
}
