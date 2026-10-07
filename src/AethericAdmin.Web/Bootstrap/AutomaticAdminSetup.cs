using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Components;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AethericAdmin.Web.Bootstrap;

public static class AutomaticAdminSetup
{
    public static async Task<bool> PrepareAsync(WebApplicationBuilder builder, CancellationToken ct = default)
    {
        var configuration = builder.Configuration;
        RegistryBootstrapState? saved = null;
        var directory = configuration["BootstrapConnection:StateDirectory"] ?? "data/bootstrap";
        try { saved = await new FileRegistryBootstrapStore(directory).ReadAsync(ct); }
        catch (FileNotFoundException) { }
        // Existing state remains authoritative. Fill missing deployment settings without resetting identity.
        var issuer = saved?.Settings.Issuer ?? configuration["Keycloak:Authority"] ?? "";
        var realm = configuration["Keycloak:Realm"] ?? "";
        var authority = issuer;
        var realmIndex = issuer.LastIndexOf("/realms/", StringComparison.Ordinal);
        if (realmIndex >= 0)
        {
            authority = issuer[..realmIndex];
            realm = Uri.UnescapeDataString(issuer[(realmIndex + 8)..].TrimEnd('/'));
        }
        void Default(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]) && !string.IsNullOrWhiteSpace(value)) configuration[key] = value;
        }
        Default("BootstrapConnection:Authority", authority);
        Default("BootstrapConnection:Realm", realm);
        if (saved is not null) configuration["BootstrapConnection:ClientId"] = saved.Settings.ClientId;
        else Default("BootstrapConnection:ClientId", configuration["Keycloak:ClientId"]);
        Default("BootstrapConnection:AdminRole", saved?.Settings.AdminRole ?? "forge-admin");
        // Public origin is deployment-owned, never inferred from an untrusted Host header.
        Default("BootstrapConnection:PublicOrigin", configuration["Admin:PublicOrigin"]);
        configuration["Bootstrap:Automatic"] = "true";
        return saved is null;
    }

    public static void AddLegacyInfrastructureResume(this IServiceCollection services)
    {
        // Reuse existing encrypted credentials, but require the selected administrator to
        // verify every connection before newly establishing infrastructure progress.
        services.Replace(ServiceDescriptor.Singleton<IInfrastructureStateStore>(sp =>
            new ResumableInfrastructureStateStore(
                new FileInfrastructureStateStore(sp.GetRequiredService<IConfiguration>()["BootstrapConnection:StateDirectory"] ?? "data/bootstrap"),
                sp.GetRequiredService<IRegistryBootstrapStore>())));
    }

    public static void MapAutomaticSetup(this WebApplication app)
    {
        app.MapGet("/university", () => Results.Redirect("/setup"));
        app.MapGet("/setup/readiness", async (HttpContext context, IInfrastructureStateStore infrastructure,
            IRegistryBootstrapStore registry, IRootCredentialStore credentials) =>
        {
            var state = await infrastructure.ReadAsync(context.RequestAborted);
            var identity = await registry.ReadAsync(context.RequestAborted);
            var complete = state?.Completed == true && identity.Phase == RegistryBootstrapPhase.Completed
                && state.Deployment == identity.Settings && state.SubjectId == identity.SubjectId
                && await AdminSetupReadiness.HasRootCredentialsAsync(credentials, context.RequestAborted);
            if (complete)
            {
                // Give the completed page its response, then rebuild the host in normal mode.
                app.Configuration["Bootstrap:HandoffRequested"] = "true";
                context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
            }
            return Results.Json(new { ready = false, restarting = complete });
        }).AllowAnonymous();
        app.MapGet("/setup/handoff.js", () => Results.Text("""
            if (location.pathname === '/setup/complete') {
                const message = document.createElement('p');
                message.setAttribute('role', 'status');
                message.textContent = 'Setup complete. Opening University setup…';
                document.querySelector('main')?.append(message);
                const check = async () => {
                    try {
                        const response = await fetch('/setup/readiness', { cache: 'no-store' });
                        if (response.ok && (await response.json()).ready) { location.replace('/university'); return; }
                    } catch { /* The host is restarting after setup. */ }
                    setTimeout(check, 1000);
                };
                check();
            }
            """, "text/javascript")).AllowAnonymous();
    }
}

public sealed class ResumableInfrastructureStateStore(IInfrastructureStateStore inner, IRegistryBootstrapStore registry) : IInfrastructureStateStore
{
    public Task<IAsyncDisposable> AcquireAsync(CancellationToken ct) => inner.AcquireAsync(ct);
    public Task SaveAsync(InfrastructureState state, CancellationToken ct) => inner.SaveAsync(state, ct);
    public async Task<InfrastructureState?> ReadAsync(CancellationToken ct)
    {
        var state = await inner.ReadAsync(ct);
        if (state is not null) return state;
        var identity = await registry.ReadAsync(ct);
        // Read-only until the verified administrator saves tested connections. Existing
        // completed or mismatched state is never erased or reopened here.
        return identity.Phase == RegistryBootstrapPhase.Completed
            ? new(identity.Settings, identity.SubjectId!, false, null) : null;
    }
}
