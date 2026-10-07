using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Providers;
using AethericForge.Runtime.Providers.Post.RabbitMq;

namespace AethericAdmin.Web.Provisioning;

// Retains result subscriptions when the operator corrects or rotates the broker connection.
public sealed class ConfiguredRabbitMqPostProvider(RabbitMqConnections connections) : BackgroundService, IPostProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<(IPostReference Reference, IMessageConsumer Consumer)> _subscriptions = [];
    private RabbitMqPostProvider? _provider;
    private string? _url;
    public string Name => ProvisioningPost.Domain;

    private async Task<RabbitMqPostProvider> ProviderAsync(CancellationToken ct)
    {
        var url = await connections.BrokerUrlAsync(ct);
        if (_provider is not null && _url == url) return _provider;
        if (_provider is not null) await _provider.DisposeAsync();
        _provider = null;
        _url = null;
        var replacement = new RabbitMqPostProvider(Name, url);
        try
        {
            foreach (var subscription in _subscriptions)
                await replacement.SubscribeAsync(subscription.Reference, subscription.Consumer, ct);
            _provider = replacement;
            _url = url;
            return replacement;
        }
        catch { await replacement.DisposeAsync(); throw; }
    }

    public async Task PublishAsync(IPostEnvelope envelope, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await (await ProviderAsync(ct)).PublishAsync(envelope, ct); }
        finally { _gate.Release(); }
    }
    public async Task SubscribeAsync(IPostReference reference, IMessageConsumer consumer, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_provider is not null) await _provider.SubscribeAsync(reference, consumer, ct);
            _subscriptions.Add((reference, consumer));
        }
        finally { _gate.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Setup remains available while credentials are missing or the broker is unavailable.
        // Retry subscriptions after saving settings and resume receiving results after restarts.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _gate.WaitAsync(stoppingToken);
                try { if (_subscriptions.Count > 0) await ProviderAsync(stoppingToken); }
                finally { _gate.Release(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { /* A submission reports connection failures; never log credential URLs. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await _gate.WaitAsync();
        try { if (_provider is not null) await _provider.DisposeAsync(); }
        finally { _gate.Release(); }
    }
}
