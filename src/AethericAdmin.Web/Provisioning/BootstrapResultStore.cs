using System.Collections.Concurrent;
using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// Holds the most recent InstitutionBootstrapCompleted per request, in process memory only - this
/// is a thin first surface for the operator-triggered action, not a durable result log. Mirrors
/// CampusDeploymentResultStore/CampusDeploymentResultConsumer exactly.
/// </summary>
public sealed class BootstrapResultStore
{
    private readonly ConcurrentDictionary<Guid, InstitutionBootstrapCompleted> _results = new();

    public void Record(InstitutionBootstrapCompleted result) => _results[result.RequestId] = result;

    public InstitutionBootstrapCompleted? TryGet(Guid requestId) =>
        _results.TryGetValue(requestId, out var result) ? result : null;
}

public sealed class BootstrapResultConsumer(BootstrapResultStore store)
    : MessageConsumerBase<InstitutionBootstrapCompleted>
{
    public override IPostContract Contract => ProvisioningBootstrapPost.ResultReference().Contract;

    public override Task ConsumeAsync(
        InstitutionBootstrapCompleted message,
        IPostContext context,
        CancellationToken ct = default)
    {
        store.Record(message);
        return Task.CompletedTask;
    }
}
