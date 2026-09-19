using System.Collections.Concurrent;
using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// Holds the most recent CampusDeploymentCompleted per request, in process memory only - this
/// is a thin first surface for the operator-triggered action, not a durable result log.
/// </summary>
public sealed class CampusDeploymentResultStore
{
    private readonly ConcurrentDictionary<Guid, CampusDeploymentCompleted> _results = new();

    public void Record(CampusDeploymentCompleted result) => _results[result.RequestId] = result;

    public CampusDeploymentCompleted? TryGet(Guid requestId) =>
        _results.TryGetValue(requestId, out var result) ? result : null;
}

/// <summary>
/// Takes the store directly rather than resolving it through DI at AddPostSubscription time -
/// AddPostSubscription registers a concrete consumer instance, not a factory, so this consumer
/// is built once during AddForgeCampus with the same store instance also registered for the
/// rest of the app (e.g. a future result-reading UI/endpoint) to share.
/// </summary>
public sealed class CampusDeploymentResultConsumer(CampusDeploymentResultStore store)
    : MessageConsumerBase<CampusDeploymentCompleted>
{
    public override IPostContract Contract => ProvisioningPost.ResultReference().Contract;

    public override Task ConsumeAsync(
        CampusDeploymentCompleted message,
        IPostContext context,
        CancellationToken ct = default)
    {
        store.Record(message);
        return Task.CompletedTask;
    }
}
