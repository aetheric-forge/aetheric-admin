using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Engine;
using AethericAdmin.Web.Hosting;

namespace AethericAdmin.Web.Provisioning;

public sealed class RabbitMqConnections(IRootCredentialStore store, IConfiguration configuration)
{
    public const string BrokerKey = "rabbitmq-amqp";
    public Task<RootCredential?> ReadManagementAsync(CancellationToken ct = default) => store.TryReadAsync("rabbitmq", ct);
    public Task<RootCredential?> ReadBrokerAsync(CancellationToken ct = default) => store.TryReadAsync(BrokerKey, ct);

    public async Task SaveManagementAsync(string url, string username, string password, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP or HTTPS management base URL without credentials, query, or fragment.");
        var credential = InfrastructureConnections.Normalize("rabbitmq", new(uri.Host, uri.Port, username, password)
        { RabbitMq = new(uri.Scheme, uri.AbsolutePath) });
        await store.SetAsync("rabbitmq", credential, ct);
    }

    public async Task SaveBrokerAsync(string host, int port, bool tls, string vhost, string username, string password, CancellationToken ct = default)
    {
        // Reuse infrastructure validation for host, port and credentials; AMQP options stay separate.
        var credential = InfrastructureConnections.Normalize("rabbitmq", new(host, port, username, password));
        if (string.IsNullOrWhiteSpace(vhost) || vhost.Length > 500 || vhost.Any(char.IsControl))
            throw new ArgumentException("Enter the Operations virtual host.");
        await store.SetAsync(BrokerKey, credential with { RabbitMq = new(tls ? "amqps" : "amqp", vhost) }, ct);
    }

    public async Task<string> BrokerUrlAsync(CancellationToken ct)
    {
        var saved = await ReadBrokerAsync(ct);
        if (saved is null) return ForgeCampusExtensions.BuildRabbitMqUrl(configuration);
        var options = saved.RabbitMq ?? throw new InvalidDataException("Saved AMQP settings are missing.");
        if (options.Scheme is not ("amqp" or "amqps")) throw new InvalidDataException("Saved AMQP scheme is invalid.");
        return new UriBuilder(options.Scheme, saved.Host, saved.Port)
        {
            UserName = saved.Username, Password = saved.Password, Path = Uri.EscapeDataString(options.BasePath)
        }.Uri.ToString();
    }
}
