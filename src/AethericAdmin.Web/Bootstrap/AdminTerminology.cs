using Aetheric.Provisioning.Components;
using Aetheric.Provisioning.Components.Terminology;

namespace AethericAdmin.Web.Bootstrap;

public static class AdminTerminology
{
    /// <summary>
    /// Persists the chosen terminology next to the other bootstrap state, so it lives on the same
    /// volume and moves with it.
    /// </summary>
    public static IServiceCollection AddAdminTerminology(this IServiceCollection services, IConfiguration configuration)
    {
        var directory = configuration["BootstrapConnection:StateDirectory"] ?? "data/bootstrap";
        services.AddSingleton<ITerminologyStore>(new FileTerminologyStore(Path.Combine(directory, "terminology.json")));
        return services.AddTerminology();
    }

    /// <summary>Loads the saved terms once so pages can read them synchronously.</summary>
    public static Task LoadTerminologyAsync(this IServiceProvider services) =>
        services.GetRequiredService<TerminologyService>().EnsureLoadedAsync();
}
