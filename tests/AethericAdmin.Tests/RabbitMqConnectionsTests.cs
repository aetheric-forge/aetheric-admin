using Aetheric.Provisioning.Persistence;
using AethericAdmin.Web.Provisioning;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class RabbitMqConnectionsTests
{
    [Fact]
    public async Task Connections_persist_separately_encrypted_and_broker_changes_are_used()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rabbitmq-entry-" + Guid.NewGuid().ToString("N"));
        var store = new ManagedRootCredentialStore(Path.Combine(directory, "credentials"), Path.Combine(directory, "key"));
        var connections = new RabbitMqConnections(store, new ConfigurationBuilder().Build());
        await connections.SaveManagementAsync("https://management.example:15671/rabbit/", "admin", "management-secret");
        await connections.SaveBrokerAsync("broker.example", 5671, true, "/operations", "sender", "broker-secret");
        var reopened = new RabbitMqConnections(new ManagedRootCredentialStore(Path.Combine(directory, "credentials"), Path.Combine(directory, "key")), new ConfigurationBuilder().Build());
        var management = await reopened.ReadManagementAsync();
        Assert.Equal(15671, management!.Port);
        Assert.Equal("/rabbit/", management.RabbitMq!.BasePath);
        Assert.Equal("management-secret", management.Password);
        var url = new Uri(await reopened.BrokerUrlAsync(default));
        Assert.Equal("amqps", url.Scheme);
        Assert.Equal(5671, url.Port);
        Assert.Equal("/operations", Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/')));
        Assert.Equal("sender:broker-secret", url.UserInfo);
        await connections.SaveBrokerAsync("corrected.example", 5672, false, "/", "sender", "replacement-secret");
        Assert.Equal("corrected.example", new Uri(await reopened.BrokerUrlAsync(default)).Host);
        foreach (var path in Directory.GetFiles(Path.Combine(directory, "credentials"), "*.credential"))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain("management-secret", System.Text.Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain("replacement-secret", System.Text.Encoding.UTF8.GetString(bytes));
        }
    }
    [Fact]
    public async Task Result_subscriptions_can_register_before_credentials_exist()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rabbitmq-lazy-" + Guid.NewGuid().ToString("N"));
        var connections = new RabbitMqConnections(new ManagedRootCredentialStore(Path.Combine(directory, "credentials"), Path.Combine(directory, "key")), new ConfigurationBuilder().Build());
        await using var provider = new ConfiguredRabbitMqPostProvider(connections);
        await provider.SubscribeAsync(ProvisioningPost.ResultReference(), new CampusDeploymentResultConsumer(new CampusDeploymentResultStore()));
        Assert.Equal(ProvisioningPost.Domain, provider.Name);
    }
    [Theory]
    [InlineData("https://admin:secret@management.example")]
    [InlineData("https://management.example?password=secret")]
    [InlineData("amqp://management.example")]
    public async Task Management_rejects_credentials_in_urls(string url)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rabbitmq-invalid-" + Guid.NewGuid().ToString("N"));
        var connections = new RabbitMqConnections(new ManagedRootCredentialStore(Path.Combine(directory, "credentials"), Path.Combine(directory, "key")), new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<ArgumentException>(() => connections.SaveManagementAsync(url, "admin", "secret"));
        Assert.Null(await connections.ReadManagementAsync());
    }
}
