using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Persistence;
using AethericAdmin.Web.Bootstrap;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class OperationalConfigurationTests
{
    private static (string StateDirectory, string CredentialsDirectory, string KeyDirectory) Directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "aetheric-admin-operational-config-" + Guid.NewGuid().ToString("N"));
        return (Path.Combine(root, "bootstrap"), Path.Combine(root, "root-credentials"), Path.Combine(root, "root-key"));
    }

    private static WebApplicationBuilder Builder(string stateDirectory, string credentialsDirectory, string keyDirectory, params (string Key, string Value)[] extra)
    {
        var builder = WebApplication.CreateBuilder();
        var values = new Dictionary<string, string?>
        {
            ["BootstrapConnection:StateDirectory"] = stateDirectory,
            ["RootCredentials:Directory"] = credentialsDirectory,
            ["RootCredentials:KeyDirectory"] = keyDirectory,
        };
        foreach (var (key, value) in extra) values[key] = value;
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    private static async Task CompleteBootstrapAsync(string stateDirectory, string issuer = "https://sso.example.org/realms/forge", string clientId = "aetheric-admin")
    {
        var store = new FileRegistryBootstrapStore(stateDirectory);
        var settings = new RegistryBootstrapSettings(issuer, clientId, "forge-admin");
        await store.InitializeAsync(settings);
        await store.SaveAsync(new(settings, RegistryBootstrapPhase.AuthorityAssigned, "operator-subject"), default);
        await store.SaveAsync(new(settings, RegistryBootstrapPhase.Completed, "operator-subject"), default);
    }

    private static async Task SetCredentialAsync(string credentialsDirectory, string keyDirectory, string system, RootCredential credential)
    {
        var store = new ManagedRootCredentialStore(credentialsDirectory, keyDirectory);
        await store.SetAsync(system, credential, default);
    }

    [Fact]
    public async Task Bootstrap_not_yet_run_fails_clearly_when_keycloak_is_needed()
    {
        var (state, credentials, key) = Directories();
        var builder = Builder(state, credentials, key);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.ApplyDerivedDefaultsAsync());
        Assert.Contains("Bootstrap has not been run", ex.Message);
    }

    [Fact]
    public async Task Bootstrap_incomplete_fails_clearly()
    {
        var (state, credentials, key) = Directories();
        var settings = new RegistryBootstrapSettings("https://sso.example.org/realms/forge", "aetheric-admin", "forge-admin");
        var store = new FileRegistryBootstrapStore(state);
        await store.InitializeAsync(settings);
        var builder = Builder(state, credentials, key);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.ApplyDerivedDefaultsAsync());
        Assert.Contains("Bootstrap is not complete", ex.Message);
    }

    [Fact]
    public async Task Missing_root_credential_fails_clearly_naming_the_system()
    {
        var (state, credentials, key) = Directories();
        await CompleteBootstrapAsync(state);
        var builder = Builder(state, credentials, key);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.ApplyDerivedDefaultsAsync());
        Assert.Contains("'rabbitmq'", ex.Message);
    }

    [Fact]
    public async Task Complete_state_derives_every_expected_value()
    {
        var (state, credentials, key) = Directories();
        await CompleteBootstrapAsync(state, issuer: "https://sso.example.org/realms/forge", clientId: "aetheric-admin");
        await SetCredentialAsync(credentials, key, "rabbitmq", new("rabbitmq.internal", 15672, "root", "rabbit-secret") { RabbitMq = new("https", "/") });
        await SetCredentialAsync(credentials, key, "redis", new("redis.internal", 6379, null, "redis-secret"));
        await SetCredentialAsync(credentials, key, "mongo", new("mongo.internal", 27017, "root", "mongo-secret") { Mongo = new("admin", true) });

        var builder = Builder(state, credentials, key);
        await builder.ApplyDerivedDefaultsAsync();
        var configuration = builder.Configuration;

        Assert.Equal("https://sso.example.org/realms/forge", configuration["Keycloak:Authority"]);
        Assert.Equal("aetheric-admin", configuration["Keycloak:ClientId"]);

        Assert.Equal("rabbitmq.internal", configuration["RabbitMq:Host"]);
        Assert.Equal("root", configuration["RabbitMq:Username"]);
        Assert.Equal("rabbit-secret", configuration["RabbitMq:Password"]);
        Assert.Equal("True", configuration["RabbitMq:Ssl"]);
        Assert.Equal("/", configuration["RabbitMq:VirtualHost"]);

        Assert.Equal("redis.internal", configuration["Redis:Host"]);
        Assert.Equal("6379", configuration["Redis:Port"]);
        Assert.Equal("redis-secret", configuration["Workbench:Redis:Password"]);
        Assert.Equal("redis-secret", configuration["ForgeCampus:Redis:Password"]);

        Assert.Equal("mongo.internal", configuration["Maintenance:MongoDb:Host"]);
        Assert.Equal("aetheric-admin", configuration["Maintenance:MongoDb:DatabaseName"]);
        Assert.Equal("admin", configuration["Maintenance:MongoDb:AuthenticationDatabase"]);
        Assert.Equal("mongo.internal", configuration["Membership:MongoDb:Host"]);
        Assert.Equal("aetheric-membership", configuration["Membership:MongoDb:DatabaseName"]);
    }

    [Fact]
    public async Task Explicit_configuration_is_never_overridden()
    {
        var (state, credentials, key) = Directories();
        await CompleteBootstrapAsync(state);
        await SetCredentialAsync(credentials, key, "rabbitmq", new("rabbitmq.internal", 15672, "root", "rabbit-secret") { RabbitMq = new() });
        await SetCredentialAsync(credentials, key, "redis", new("redis.internal", 6379, null, "redis-secret"));
        await SetCredentialAsync(credentials, key, "mongo", new("mongo.internal", 27017, "root", "mongo-secret") { Mongo = new("admin", true) });

        var builder = Builder(state, credentials, key, ("RabbitMq:Host", "already-configured.example"));
        await builder.ApplyDerivedDefaultsAsync();

        Assert.Equal("already-configured.example", builder.Configuration["RabbitMq:Host"]);
        // Everything else still derives normally.
        Assert.Equal("redis.internal", builder.Configuration["Redis:Host"]);
    }
}
