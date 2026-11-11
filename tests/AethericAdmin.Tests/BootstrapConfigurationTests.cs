using AethericAdmin.Web.Bootstrap;
using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class BootstrapConfigurationTests
{
    [Fact]
    public async Task Private_admin_endpoint_is_deployment_configured_without_changing_saved_issuer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "admin-private-api-" + Guid.NewGuid().ToString("N"));
        try
        {
            var builder = Builder(directory);
            builder.Configuration["BootstrapConnection:AdminApiBaseAddress"] = "https://admin.internal.example/admin/";
            await builder.AddAdminBootstrapAsync(initialize: true);
            await using var provider = builder.Services.BuildServiceProvider();
            var configuration = provider.GetRequiredService<Aetheric.Provisioning.Components.BootstrapConnectionConfiguration>();
            Assert.Equal("https://admin.internal.example/admin/", configuration.Options("provisioner", "test-secret").AdminApiBaseAddress);
            Assert.Equal("https://identity.example/realms/root", configuration.Settings.Issuer);
            Assert.Equal(configuration.Settings, (await provider.GetRequiredService<IRegistryBootstrapStore>().ReadAsync(default)).Settings);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Startup_requires_explicit_initialization_and_never_reopens_completed_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), "admin-bootstrap-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Builder(directory).AddAdminBootstrapAsync());
            var initialize = Builder(directory);
            await initialize.AddAdminBootstrapAsync(initialize: true);
            await using (var services = initialize.Services.BuildServiceProvider())
            {
                var store = services.GetRequiredService<IRegistryBootstrapStore>();
                var state = await store.ReadAsync(default);
                await store.SaveAsync(state with { Phase = RegistryBootstrapPhase.Completed, SubjectId = "selected-admin" }, default);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => Builder(directory).AddAdminBootstrapAsync(initialize: true));
            var restart = Builder(directory);
            await restart.AddAdminBootstrapAsync();
            await using (var services = restart.Services.BuildServiceProvider())
                Assert.Equal(RegistryBootstrapPhase.Completed, (await services.GetRequiredService<IRegistryBootstrapStore>().ReadAsync(default)).Phase);
            var mismatch = Builder(directory);
            mismatch.Configuration["BootstrapConnection:ClientId"] = "different-client";
            await mismatch.AddAdminBootstrapAsync();
            await using (var services = mismatch.Services.BuildServiceProvider())
                Assert.Equal("provisioner", services.GetRequiredService<Aetheric.Provisioning.Components.BootstrapConnectionConfiguration>().ClientId);
            var wrongRealm = Builder(directory);
            wrongRealm.Configuration["BootstrapConnection:Realm"] = "different-realm";
            await Assert.ThrowsAsync<InvalidOperationException>(() => wrongRealm.AddAdminBootstrapAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static WebApplicationBuilder Builder(string directory)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["BootstrapConnection:Authority"] = "https://identity.example",
            ["BootstrapConnection:Realm"] = "root",
            ["BootstrapConnection:ClientId"] = "provisioner",
            ["BootstrapConnection:PublicOrigin"] = "http://127.0.0.1:5180",
            ["BootstrapConnection:StateDirectory"] = Path.Combine(directory,"state"),
            ["RootCredentials:Directory"] = Path.Combine(directory,"credentials"),
            ["RootCredentials:KeyDirectory"] = Path.Combine(directory,"key"),
            ["Bootstrap:ProtectionKeyDirectory"] = Path.Combine(directory,"protection")
        });
        return builder;
    }
}
