using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Components;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Infrastructure;
using Aetheric.Provisioning.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace AethericAdmin.Web.Bootstrap;

public static class AdminBootstrapHosting
{
    public static async Task<AdministratorSignInConfiguration> AddAdminBootstrapAsync(this WebApplicationBuilder builder, bool initialize = false)
    {
        var connection = builder.Configuration.GetSection("BootstrapConnection").Get<BootstrapConnectionConfiguration>() ?? new();
        var signIn = new AdministratorSignInConfiguration(connection, builder.Environment.IsDevelopment());
        if (!connection.IsConfigured || !signIn.Enabled)
            throw new InvalidOperationException("Bootstrap mode requires BootstrapConnection:Authority, Realm, ClientId and a valid PublicOrigin.");
        // Validate the deployment-owned destination before initializing its durable state.
        using var validation = new Aetheric.Provisioning.Registry.KeycloakAdministratorCreator(
            connection.Options(connection.ClientId, "configuration-validation"), connection.AdminRole);
        var store = new FileRegistryBootstrapStore(connection.StateDirectory);
        if (initialize) await store.InitializeAsync(connection.Settings);
        else
        {
            RegistryBootstrapState state;
            try { state = await store.ReadAsync(default); }
            catch (FileNotFoundException)
            { throw new InvalidOperationException("Bootstrap state is missing. Initialize this deployment once with --initialize-bootstrap, or restore its existing state."); }
            if (state.Settings != connection.Settings)
                throw new InvalidOperationException("Bootstrap deployment settings changed. Restore the matching configuration and state.");
        }
        builder.Services.AddSingleton<IRegistryBootstrapStore>(store);
        builder.Services.AddSingleton<IInfrastructureStateStore>(new FileInfrastructureStateStore(connection.StateDirectory));
        builder.Services.AddSingleton<IRootCredentialStore>(new ManagedRootCredentialStore(
            builder.Configuration["RootCredentials:Directory"] ?? "data/root-credentials",
            builder.Configuration["RootCredentials:KeyDirectory"] ?? "data/root-key"));
        builder.Services.AddSingleton<IRootConnectionValidator, RootConnectionValidator>();
        builder.Services.AddProvisioningBootstrap(connection);
        builder.Services.AddProvisioningSimulation();
        builder.AddSetupAuthentication(connection, signIn);
        // Bootstrap cannot depend on the Redis connection it is about to collect.
        var protectionDirectory = Directory.CreateDirectory(
            builder.Configuration["Bootstrap:ProtectionKeyDirectory"] ?? "data/bootstrap-protection");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(protectionDirectory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        builder.Services.AddDataProtection().SetApplicationName("AethericAdmin.Bootstrap")
            .PersistKeysToFileSystem(protectionDirectory);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        return signIn;
    }

    public static void MapAdminBootstrap(this WebApplication app, AdministratorSignInConfiguration signIn)
    {
        app.MapGet("/", () => Results.Redirect("/setup"));
        app.MapSetupAuthentication(signIn);
        app.MapInfrastructure();
        // This root component lives in a dedicated assembly: normal admin pages are not exposed
        // and cannot instantiate their Redis/Mongo dependencies while bootstrap is active.
        app.MapRazorComponents<AethericAdmin.BootstrapShell.BootstrapApp>()
            .AddAdditionalAssemblies(typeof(ProvisioningComponentAssembly).Assembly)
            .AddInteractiveServerRenderMode();
    }
}
