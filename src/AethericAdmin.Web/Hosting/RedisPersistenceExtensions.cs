using AethericAdmin.Web.Infrastructure;
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
        var host = resolved["Redis:Host"];
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException($"{institution}:Redis:Host is required.");
        var port = resolved.GetValue<int?>("Redis:Port") ?? 6379;
        var database = resolved.GetValue<int?>("Redis:Database") ?? 0;
        if (port is < 1 or > 65535 || database < 0) throw new InvalidOperationException("Invalid Redis port or database.");
        return new ConfigurationOptions
        {
            EndPoints = { { host, port } },
            User = resolved["Redis:User"],
            Password = resolved["Redis:Password"],
            Ssl = resolved.GetValue<bool>("Redis:Ssl"),
            DefaultDatabase = database,
            AbortOnConnectFail = true,
            ConnectRetry = 1,
            ConnectTimeout = 5000
        };
    }
}
