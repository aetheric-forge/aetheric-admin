namespace AethericAdmin.Web.Maintenance.Jobs;

public interface IJobDefinitionStore
{
    Task<JobDefinition> AddAsync(
        string name,
        JobDefinitionKind kind,
        string? cronSchedule,
        SshJobConfig? ssh,
        CodeJobConfig? code,
        CancellationToken ct = default);

    Task<IReadOnlyList<JobDefinition>> ListAsync(CancellationToken ct = default);

    Task<IReadOnlyList<JobDefinition>> ListEnabledAsync(CancellationToken ct = default);

    Task<JobDefinition?> GetAsync(Guid id, CancellationToken ct = default);

    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
