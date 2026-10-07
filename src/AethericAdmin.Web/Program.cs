using AethericAdmin.Web.Marketing;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Persistence;
using AethericAdmin.Web.Components;
using AethericAdmin.Web.Bootstrap;
using AethericAdmin.Web.Hosting;
using AethericAdmin.Web.Maintenance;
using AethericAdmin.Web.Maintenance.Jobs;
using AethericAdmin.Web.Provisioning;
using AethericContracts.Membership;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Forge.Primitives.MongoDb;
using System.Net;

// MongoDB.Driver 3.x removed its old implicit Guid-serialization default - without this, every
// write of a Guid Id (JobDefinition, SshCredential) throws "GuidSerializer cannot serialize a Guid
// when GuidRepresentation is Unspecified." Must run before any Mongo store is constructed.
MongoBsonSetup.EnsureGuidRepresentationRegistered();

var initializeBootstrap = args.Contains("--initialize-bootstrap", StringComparer.Ordinal);
while (true)
{
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--initialize-bootstrap").ToArray());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<UniversityDraftSession>();

var explicitBootstrap = initializeBootstrap || builder.Configuration.GetValue<bool>("Bootstrap:Enabled");
var credentialStore = new ManagedRootCredentialStore(
    builder.Configuration["RootCredentials:Directory"] ?? "data/root-credentials",
    builder.Configuration["RootCredentials:KeyDirectory"] ?? "data/root-key");
var automaticBootstrap = !explicitBootstrap
    && builder.Configuration.GetValue("Bootstrap:AutoSetup", !builder.Environment.IsDevelopment())
    && !await AdminSetupReadiness.HasRootCredentialsAsync(credentialStore);

// Incomplete deployments expose only the existing verified setup workflow, without
// starting the operational Redis/Mongo services whose credentials it collects.
if (explicitBootstrap || automaticBootstrap)
{
    var initialize = initializeBootstrap;
    if (automaticBootstrap) initialize = await AutomaticAdminSetup.PrepareAsync(builder);
    var signIn = await builder.AddAdminBootstrapAsync(initialize);
    if (automaticBootstrap) builder.Services.AddLegacyInfrastructureResume();
    if (initializeBootstrap)
    {
        Console.WriteLine("Initialized admin bootstrap state. Existing state is never replaced.");
        return;
    }
    await using var bootstrapApp = builder.Build();
    bootstrapApp.UseForwardedHeaders();
    if (!bootstrapApp.Environment.IsDevelopment())
    {
        bootstrapApp.UseExceptionHandler("/error");
        bootstrapApp.UseHsts();
        bootstrapApp.UseHttpsRedirection();
    }
    bootstrapApp.UseStaticFiles();
    bootstrapApp.UseAuthentication();
    bootstrapApp.UseAuthorization();
    bootstrapApp.UseAntiforgery();
    bootstrapApp.MapAdminBootstrap(signIn);
    if (automaticBootstrap) bootstrapApp.MapAutomaticSetup();
    await bootstrapApp.RunAsync();
    if (automaticBootstrap && builder.Configuration.GetValue<bool>("Bootstrap:HandoffRequested")) continue;
    return;
}

// In Development, skip OIDC entirely so UI work isn't blocked on a live Keycloak client.
// Everywhere else every page requires authentication via the global fallback policy below -
// this whole app is admin-only, there's no tiered public/member/maintainer policy to reproduce.
var requireAuth = !builder.Environment.IsDevelopment();

// Fills in Keycloak/RabbitMq/Redis/Maintenance+Membership Mongo config from state the bootstrap
// flow already collected, wherever the operator hasn't explicitly set it - see
// OperationalConfiguration's own doc comment for why this is safe to derive rather than requiring
// it to be hand-typed a second time.
await builder.ApplyDerivedDefaultsAsync();

if (requireAuth && !await OperationalClientSecret.ApplyAsync(builder.Configuration,
    new ManagedRootCredentialStore(
        builder.Configuration["RootCredentials:Directory"] ?? "data/root-credentials",
        builder.Configuration["RootCredentials:KeyDirectory"] ?? "data/root-key")))
{
    // Keep a usable diagnostic page available without exposing operational routes.
    var setupRequired = builder.Build();
    setupRequired.MapGet("/{**path}", () => Results.Content(
        "<!doctype html><html><head><title>Admin login setup required</title></head><body>" +
        "<h1>Admin login setup required</h1><p>The saved provisioner client secret is missing " +
        "or does not match this deployment.</p><p>Restart in bootstrap mode, open /setup, " +
        "and reconnect the provisioner client, then sign in as the existing administrator. " +
        "Your infrastructure credentials are retained. Restart normal mode afterwards.</p></body></html>",
        "text/html", statusCode: StatusCodes.Status503ServiceUnavailable));
    await setupRequired.RunAsync();
    return;
}


if (requireAuth)
{
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddOpenIdConnect(options =>
        {
            // A derived Authority is already the full issuer URL (state.Settings.Issuer);
            // an explicitly-configured Authority is still the older bare-authority-plus-realm
            // shape - detect which one we have rather than requiring every deployment to move
            // to the combined form.
            var authority = builder.Configuration["Keycloak:Authority"] ?? "";
            options.Authority = authority.Contains("/realms/", StringComparison.Ordinal)
                ? authority
                : $"{authority}/realms/{builder.Configuration["Keycloak:Realm"]}";
            options.ClientId = builder.Configuration["Keycloak:ClientId"];
            options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
            options.CallbackPath = builder.Configuration["Keycloak:CallbackPath"] ?? "/signin-oidc";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            // Safari over the operator's SSH loopback tunnel must return correlation/nonce
            // cookies on the top-level OIDC callback. Lax works with the query response mode;
            // SameAsRequest permits localhost HTTP while retaining Secure cookies under HTTPS.
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                if (!context.Request.IsHttps
                    && !(context.Request.Host.Host == "localhost"
                        || IPAddress.TryParse(context.Request.Host.Host, out var address) && IPAddress.IsLoopback(address)))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "text/plain";
                    context.HandleResponse();
                    return context.Response.WriteAsync("Admin sign-in requires HTTPS except on loopback.");
                }
                return Task.CompletedTask;
            };
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
        });

    builder.Services.AddAuthorization(options =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
}

builder.Services.AddAdminRedisPersistence(builder.Configuration);
builder.Services.AddForgeCampus();

// Bootstrap-mode-only until now (AdminBootstrapHosting.cs) - normal-mode admin needs this too,
// to read back the root credentials it collected during bootstrap and forward them in the
// CampusDeploymentRequested message. Same directories, same encrypted file format.
builder.Services.AddSingleton<IRootCredentialStore>(new ManagedRootCredentialStore(
    builder.Configuration["RootCredentials:Directory"] ?? "data/root-credentials",
    builder.Configuration["RootCredentials:KeyDirectory"] ?? "data/root-key"));

builder.Services.AddSingleton(TimeProvider.System);

// Maintenance owns its job and encrypted-credential stores - a keyed client keeps this
// connection separate should other institutions ever join this app.
builder.Services.AddKeyedMongoClient(
    "Maintenance",
    InstitutionServiceConfiguration.Resolve(builder.Configuration, "Maintenance", "MongoDb")
        .GetSection("MongoDb").Get<MongoOptions>()!);

builder.Services.AddSingleton<ICredentialStore, MongoCredentialStore>();
builder.Services.AddSingleton<IJobDefinitionStore, MongoJobDefinitionStore>();
builder.Services.AddSingleton<IJobExecutor, SshJobExecutor>();
builder.Services.AddSingleton<IJobExecutor, CodeJobExecutor>();
builder.Services.AddSingleton<JobDispatcher>();

// Shared with aetheric-web via the aetheric-contracts submodule - aetheric-web creates
// applications through the public Join Campus form, this app reads/flags them.
builder.Services.AddKeyedMongoClient(
    "Membership",
    InstitutionServiceConfiguration.Resolve(builder.Configuration, "Membership", "MongoDb")
        .GetSection("MongoDb").Get<MongoOptions>()!);
builder.Services.AddSingleton<IMembershipApplicationStore, MongoMembershipApplicationStore>();
builder.Services.AddSingleton<IMaintenanceWorker, StaleMembershipApplicationsWorker>();

builder.Services.AddHostedService<MaintenanceDispatchService>();

builder.Services.AddMarketingManagement(builder.Configuration);

await using var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

if (requireAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapCampusDeploymentEndpoints();
app.MapGet("/setup/readiness", () => Results.Json(new { ready = true })).AllowAnonymous();
app.MapGet("/setup", () => Results.Redirect("/university"));
app.MapGet("/setup/complete", () => Results.Redirect("/university"));

app.Run();
return;
}
