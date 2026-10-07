using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Components;
using Aetheric.Provisioning.Persistence;
using Aetheric.Provisioning.Registry;
using AethericAdmin.Web.Bootstrap;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class BootstrapClientSelectionTests
{
    private sealed class Verifier : ISetupClientVerifier
    {
        public bool Fail;
        public int Calls;
        public Task VerifyAsync(BootstrapConnectionConfiguration configuration, string clientId, string secret, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new RegistryBootstrapStaffException("registry.client_authentication_failed");
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Choice_is_verified_before_saving_invalidates_old_sessions_and_survives_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "client-choice-" + Guid.NewGuid().ToString("N"));
        var configuration = new BootstrapConnectionConfiguration { Authority = "https://identity.example", Realm = "forge", ClientId = "provisioner", AllowClientSelection = true };
        var store = new FileRegistryBootstrapStore(dir);
        await store.InitializeAsync(configuration.Settings);
        using var sessions = new SetupSessions(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "protection"))));
        var bootstrap = new SetupBootstrap(configuration, store, sessions, null!, new FileInfrastructureStateStore(dir), null!);
        var verifier = new Verifier { Fail = true };
        var connection = new BootstrapConnection(configuration, sessions, bootstrap, store, verifier);
        var oldTicket = sessions.CreateTicket("old-secret");
        await Assert.ThrowsAsync<RegistryBootstrapStaffException>(() => connection.CheckAsync("my-client", "new-secret", default));
        Assert.Equal("provisioner", (await store.ReadAsync(default)).Settings.ClientId);
        Assert.Equal("provisioner", configuration.ClientId);
        var oldSession = sessions.Begin(oldTicket);
        Assert.True(sessions.IsActive(oldSession));
        verifier.Fail = false;
        var ticket = await connection.CheckAsync("my-client", "new-secret", default);
        Assert.False(sessions.IsActive(oldSession));
        Assert.Equal("my-client", (await store.ReadAsync(default)).Settings.ClientId);
        Assert.Equal("my-client", configuration.ClientId);
        Assert.Equal("new-secret", sessions.Secret(sessions.Begin(ticket)));
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapConnection:Authority"] = configuration.Authority,
            ["BootstrapConnection:Realm"] = configuration.Realm,
            ["BootstrapConnection:ClientId"] = "provisioner",
            ["BootstrapConnection:PublicOrigin"] = "https://admin.example",
            ["BootstrapConnection:StateDirectory"] = dir,
            ["RootCredentials:Directory"] = Path.Combine(dir, "credentials"),
            ["RootCredentials:KeyDirectory"] = Path.Combine(dir, "key"),
            ["Bootstrap:ProtectionKeyDirectory"] = Path.Combine(dir, "protection")
        });
        await builder.AddAdminBootstrapAsync();
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Equal("my-client", provider.GetRequiredService<BootstrapConnectionConfiguration>().ClientId);
    }
    [Fact]
    public async Task Selected_administrator_locks_client_before_network_verification()
    {
        var dir = Path.Combine(Path.GetTempPath(), "client-locked-" + Guid.NewGuid().ToString("N"));
        var configuration = new BootstrapConnectionConfiguration { Authority = "https://identity.example", Realm = "forge", ClientId = "bound-client", AllowClientSelection = true };
        var store = new FileRegistryBootstrapStore(dir);
        await store.InitializeAsync(configuration.Settings);
        await store.SaveAsync(new(configuration.Settings, RegistryBootstrapPhase.AuthorityAssigned, "selected-subject"), default);
        using var sessions = new SetupSessions(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "protection"))));
        var bootstrap = new SetupBootstrap(configuration, store, sessions, null!, new FileInfrastructureStateStore(dir), null!);
        var verifier = new Verifier();
        var connection = new BootstrapConnection(configuration, sessions, bootstrap, store, verifier);
        var ex = await Assert.ThrowsAsync<RegistryBootstrapStaffException>(() => connection.CheckAsync("other-client", "secret", default));
        Assert.Equal("registry.client_not_configured", ex.Code);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal("selected-subject", (await store.ReadAsync(default)).SubjectId);
    }
}
