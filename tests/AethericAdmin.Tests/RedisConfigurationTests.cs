using AethericAdmin.Web.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class RedisConfigurationTests
{
    [Fact]
    public void Platform_credentials_cannot_replace_institution_credentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Redis:Host"] = "localhost", ["Redis:Password"] = "platform-secret"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => RedisPersistenceExtensions.ConnectionOptions(configuration, "Workbench"));
    }

    [Fact]
    public void Key_ring_cannot_overlap_workbench_prefix()
    {
        var configuration = new ConfigurationBuilder().AddConfiguration(TestHost.Configuration("test:"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:Key"] = "test:workbench:keys" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAdminRedisPersistence(configuration));
    }
}
