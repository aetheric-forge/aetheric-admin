using Aetheric.Provisioning.Persistence;
using AethericAdmin.Web.Bootstrap;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class OperationalClientSecretTests
{
    [Fact]
    public async Task Encrypted_secret_survives_restart_and_is_bound_to_issuer_and_client()
    {
        var root = Path.Combine(Path.GetTempPath(), "admin-client-secret-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "credentials");
            var key = Path.Combine(root, "key");
            var first = new ManagedRootCredentialStore(directory, key);
            await first.SetAsync("provisioner-client", new("https://identity.example/realms/root", 443, "provisioner", "synthetic-client-secret"), default);
            Assert.DoesNotContain("synthetic-client-secret", System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(directory, "*.credential")))));
            var restarted = new ManagedRootCredentialStore(directory, key);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://identity.example/realms/root",
                ["Keycloak:ClientId"] = "provisioner"
            }).Build();
            Assert.True(await OperationalClientSecret.ApplyAsync(config, restarted));
            Assert.Equal("synthetic-client-secret", config["Keycloak:ClientSecret"]);
            Assert.Equal("/setup/signin-oidc", config["Keycloak:CallbackPath"]);
            config["Keycloak:ClientSecret"] = "";
            config["Keycloak:Authority"] = "https://other.example/realms/root";
            Assert.False(await OperationalClientSecret.ApplyAsync(config, restarted));
            Assert.Equal("", config["Keycloak:ClientSecret"]);
            config["Keycloak:Authority"] = "https://identity.example/realms/root";
            config["Keycloak:ClientId"] = "other-client";
            Assert.False(await OperationalClientSecret.ApplyAsync(config, restarted));
            config["Keycloak:ClientSecret"] = "explicit-secret";
            Assert.True(await OperationalClientSecret.ApplyAsync(config, restarted));
            Assert.Equal("explicit-secret", config["Keycloak:ClientSecret"]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Missing_secret_requests_setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "admin-client-secret-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(await OperationalClientSecret.ApplyAsync(new ConfigurationBuilder().Build(),
                new ManagedRootCredentialStore(Path.Combine(root, "credentials"), Path.Combine(root, "key"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
