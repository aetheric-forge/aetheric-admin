using AethericAdmin.Web.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AethericAdmin.Tests;

internal static class TestHost
{
    public static IConfiguration Configuration(string prefix) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Redis:Host"] = Environment.GetEnvironmentVariable("REDIS_TEST_HOST") ?? "127.0.0.1",
        ["Redis:Port"] = Environment.GetEnvironmentVariable("REDIS_TEST_PORT") ?? "6379",
        ["Workbench:Redis:Password"] = Environment.GetEnvironmentVariable("REDIS_TEST_PASSWORD") ?? "dev",
        ["ForgeCampus:Redis:Password"] = Environment.GetEnvironmentVariable("REDIS_TEST_PASSWORD") ?? "dev",
        ["Workbench:KeyPrefix"] = prefix + "workbench:",
        ["DataProtection:Key"] = prefix + "keys",
        ["DataProtection:ApplicationName"] = "AethericAdmin"
    }).Build();

    public static ServiceProvider Create(string prefix)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminRedisPersistence(Configuration(prefix));
        services.AddForgeCampus();
        return services.BuildServiceProvider();
    }
}
