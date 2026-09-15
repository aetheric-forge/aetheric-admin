using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace AethericAdmin.Web.Maintenance.Jobs;

public sealed class MongoJobDefinitionStore : IJobDefinitionStore
{
    private readonly IMongoCollection<JobDefinition> _collection;
    private readonly TimeProvider _timeProvider;

    public MongoJobDefinitionStore(
        [FromKeyedServices("Maintenance")] IMongoDatabase database,
        TimeProvider timeProvider)
    {
        _collection = database.GetCollection<JobDefinition>("maintenance-jobs");
        _timeProvider = timeProvider;
    }

    public async Task<JobDefinition> AddAsync(
        string name,
        JobDefinitionKind kind,
        string? cronSchedule,
        SshJobConfig? ssh,
        CodeJobConfig? code,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (kind == JobDefinitionKind.Ssh && ssh is null)
        {
            throw new ArgumentException("An SSH job requires SSH configuration.", nameof(ssh));
        }

        if (kind == JobDefinitionKind.Code && code is null)
        {
            throw new ArgumentException("A Code job requires Code configuration.", nameof(code));
        }

        if (!string.IsNullOrWhiteSpace(cronSchedule))
        {
            // Throws if invalid - fail fast at creation time rather than silently never firing.
            Cronos.CronExpression.Parse(cronSchedule);
        }

        var job = new JobDefinition(
            Guid.NewGuid(),
            name.Trim(),
            kind,
            string.IsNullOrWhiteSpace(cronSchedule) ? null : cronSchedule.Trim(),
            Enabled: true,
            ssh,
            code,
            _timeProvider.GetUtcNow());

        await _collection.InsertOneAsync(job, cancellationToken: ct).ConfigureAwait(false);
        return job;
    }

    public async Task<IReadOnlyList<JobDefinition>> ListAsync(CancellationToken ct = default)
    {
        return await _collection.Find(FilterDefinition<JobDefinition>.Empty)
            .SortBy(job => job.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobDefinition>> ListEnabledAsync(CancellationToken ct = default)
    {
        return await _collection.Find(job => job.Enabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<JobDefinition?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _collection.Find(job => job.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        await _collection.UpdateOneAsync(
                job => job.Id == id,
                Builders<JobDefinition>.Update.Set(job => job.Enabled, enabled),
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(job => job.Id == id, ct).ConfigureAwait(false);
    }
}
