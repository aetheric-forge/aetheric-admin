using Forge.Primitives.MongoDb;
using MarketingCampus.Application;
using MarketingCampus.Providers.MongoDb;
using Microsoft.AspNetCore.Components.Authorization;
using MongoDB.Driver;
namespace AethericAdmin.Web.Marketing;

public sealed record MarketingAvailability(bool Enabled);
public static class MarketingHosting
{
    public static IServiceCollection AddMarketingManagement(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Marketing:MongoDb").Exists()
            ? AethericAdmin.Web.Hosting.InstitutionServiceConfiguration.Resolve(configuration, "Marketing", "MongoDb").GetSection("MongoDb").Get<MongoOptions>()
            : null;
        var enabled = !string.IsNullOrWhiteSpace(options?.Host);
        services.AddSingleton(new MarketingAvailability(enabled));
        if (enabled)
        {
            services.AddMarketingDraftStorage(new MongoClient(options!.ToConnectionString()).GetDatabase(options.DatabaseName));
            services.AddScoped<ICampaignOperatorAuthorizer, MarketingOperatorAuthorizer>();
            services.AddScoped<CampaignManagementService>();
        }
        return services;
    }
}
internal sealed class MarketingOperatorAuthorizer(AuthenticationStateProvider authentication, IWebHostEnvironment environment)
    : ICampaignOperatorAuthorizer
{
    public async Task EnsureCanManageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Preserve this admin host's operator policy: authenticated access outside Development.
        if (environment.IsDevelopment()) return;
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException("Administrator sign-in is required.");
    }
}
