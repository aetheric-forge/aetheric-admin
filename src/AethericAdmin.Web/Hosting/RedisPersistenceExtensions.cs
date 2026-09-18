using AethericAdmin.Web.Infrastructure;
using Forge.Primitives.Redis;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using StackExchange.Redis;

namespace AethericAdmin.Web.Hosting;

public static class RedisPersistenceExtensions
{
    public static IServiceCollection AddAdminRedisPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var workbench = ConnectionOptions(configuration, "Workbench");
        var protection = ConnectionOptions(configuration, "ForgeCampus");
        var prefix = configuration["Workbench:KeyPrefix"];
        var applicationName = configuration["DataProtection:ApplicationName"];
        var key = configuration["DataProtection:Key"];
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(applicationName) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Workbench:KeyPrefix, DataProtection:ApplicationName and DataProtection:Key are required.");
        if (key.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Data Protection keys must be outside the Workbench key prefix.");

        services.AddKeyedSingleton<IConnectionMultiplexer>("Workbench", (_, _) => ConnectionMultiplexer.Connect(workbench));
        services.AddKeyedSingleton<IConnectionMultiplexer>("ForgeCampus", (_, _) => ConnectionMultiplexer.Connect(protection));
        services.AddSingleton(sp => new RedisWorkbenchService(
            sp.GetRequiredKeyedService<IConnectionMultiplexer>("Workbench").GetDatabase(), prefix));
        services.AddDataProtection().SetApplicationName(applicationName);
        services.AddOptions<KeyManagementOptions>().Configure<IServiceProvider>((options, sp) =>
            options.XmlRepository = new RedisXmlRepository(
                () => sp.GetRequiredKeyedService<IConnectionMultiplexer>("ForgeCampus").GetDatabase(), key));
        return services;
    }

    public static ConfigurationOptions ConnectionOptions(IConfiguration configuration, string institution)
    {
        var resolved = InstitutionServiceConfiguration.Resolve(configuration, institution, "Redis");
        var options = resolved.GetSection("Redis").Get<RedisOptions>() ?? new RedisOptions { Host = string.Empty };
        if (string.IsNullOrWhiteSpace(options.Host)) throw new InvalidOperationException($"{institution}:Redis:Host is required.");
        if (options.Port is < 1 or > 65535 || options.DefaultDatabase < 0) throw new InvalidOperationException("Invalid Redis port or database.");
        options.AbortOnConnectFail = true;
        options.ConnectRetry = 1;
        options.ConnectTimeout = 5000;
        return options.ToConfigurationOptions();
    }
}
