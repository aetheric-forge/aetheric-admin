using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Persistence;
using AethericAdmin.Web.Bootstrap;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class AutomaticAdminSetupTests
{
    private static (WebApplicationBuilder Builder, ManagedRootCredentialStore Credentials, FileRegistryBootstrapStore Registry, FileInfrastructureStateStore Infrastructure) Deployment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "automatic-admin-" + Guid.NewGuid().ToString("N"));
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapConnection:StateDirectory"] = Path.Combine(directory, "state"),
            ["RootCredentials:Directory"] = Path.Combine(directory, "credentials"),
            ["RootCredentials:KeyDirectory"] = Path.Combine(directory, "key"),
            ["Keycloak:Authority"] = "https://identity.example/realms/forge",
            ["Keycloak:ClientId"] = "provisioner",
            ["Admin:PublicOrigin"] = "https://admin.example"
        });
        return (builder, new(Path.Combine(directory, "credentials"), Path.Combine(directory, "key")),
            new(Path.Combine(directory, "state")), new(Path.Combine(directory, "state")));
    }
    [Fact]
    public async Task Partial_credentials_require_setup_and_existing_credentials_are_retained()
    {
        var (builder, credentials, _, _) = Deployment();
        var rabbit = new RootCredential("rabbit.example", 15672, "admin", "saved-secret") { RabbitMq = new() };
        await credentials.SetAsync("rabbitmq", rabbit, default);
        Assert.False(await AdminSetupReadiness.HasRootCredentialsAsync(credentials));
        Assert.True(await AutomaticAdminSetup.PrepareAsync(builder));
        await builder.AddAdminBootstrapAsync(initialize: true);
        Assert.Equal(rabbit, await credentials.TryReadAsync("rabbitmq", default));
        Assert.Equal("https://identity.example", builder.Configuration["BootstrapConnection:Authority"]);
        Assert.Equal("forge", builder.Configuration["BootstrapConnection:Realm"]);
        Assert.Equal("https://admin.example", builder.Configuration["BootstrapConnection:PublicOrigin"]);
    }
    [Fact]
    public async Task All_five_root_credentials_allow_university_without_requiring_postgres_or_amqp_override()
    {
        var (_, credentials, _, _) = Deployment();
        foreach (var system in AdminSetupReadiness.RequiredSystems)
            await credentials.SetAsync(system, new("service.example", 1234, "admin", "secret"), default);
        Assert.True(await AdminSetupReadiness.HasRootCredentialsAsync(credentials));
        Assert.Null(await credentials.TryReadAsync("rabbitmq-amqp", default));
        Assert.Null(await credentials.TryReadAsync("postgres", default));
    }
    [Fact]
    public async Task Existing_identity_and_explicit_settings_are_never_reinitialized()
    {
        var (builder, _, registry, _) = Deployment();
        var settings = new RegistryBootstrapSettings("https://saved.example/realms/original", "saved-client", "saved-role");
        await registry.InitializeAsync(settings);
        await registry.SaveAsync(new(settings, RegistryBootstrapPhase.AuthorityAssigned, "selected-subject"), default);
        builder.Configuration["BootstrapConnection:PublicOrigin"] = "https://explicit.example";
        Assert.False(await AutomaticAdminSetup.PrepareAsync(builder));
        Assert.Equal("selected-subject", (await registry.ReadAsync(default)).SubjectId);
        Assert.Equal("https://saved.example", builder.Configuration["BootstrapConnection:Authority"]);
        Assert.Equal("original", builder.Configuration["BootstrapConnection:Realm"]);
        Assert.Equal("saved-client", builder.Configuration["BootstrapConnection:ClientId"]);
        Assert.Equal("https://explicit.example", builder.Configuration["BootstrapConnection:PublicOrigin"]);
    }
    [Fact]
    public async Task Legacy_progress_requires_verified_identity_and_completed_progress_is_not_reopened()
    {
        var (_, _, registry, infrastructure) = Deployment();
        var settings = new RegistryBootstrapSettings("https://identity.example/realms/forge", "provisioner", "forge-admin");
        await registry.InitializeAsync(settings);
        var resume = new ResumableInfrastructureStateStore(infrastructure, registry);
        Assert.Null(await resume.ReadAsync(default));
        await registry.SaveAsync(new(settings, RegistryBootstrapPhase.Completed, "selected-subject"), default);
        var pending = await resume.ReadAsync(default);
        Assert.False(pending!.Completed);
        Assert.Equal("selected-subject", pending.SubjectId);
        Assert.Null(await infrastructure.ReadAsync(default));
        var completed = pending with { Completed = true, CompletedAt = DateTimeOffset.UtcNow };
        await infrastructure.SaveAsync(completed, default);
        Assert.Equal(completed, await resume.ReadAsync(default));
    }
    [Fact]
    public async Task Corrupt_credentials_do_not_look_like_a_new_deployment()
    {
        var (builder, credentials, _, _) = Deployment();
        await credentials.SetAsync("rabbitmq", new("rabbit.example", 15672, "admin", "secret"), default);
        var path = Directory.GetFiles(builder.Configuration["RootCredentials:Directory"]!, "*.credential").Single();
        await File.WriteAllTextAsync(path, "corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => AdminSetupReadiness.HasRootCredentialsAsync(credentials));
    }
}
