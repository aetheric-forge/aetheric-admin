using AethericAdmin.Web.Components;
using AethericAdmin.Web.Bootstrap;
using AethericAdmin.Web.Hosting;
using AethericAdmin.Web.Maintenance;
using AethericAdmin.Web.Maintenance.Jobs;
using AethericContracts.Membership;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Forge.Primitives.MongoDb;

// MongoDB.Driver 3.x removed its old implicit Guid-serialization default - without this, every
// write of a Guid Id (JobDefinition, SshCredential) throws "GuidSerializer cannot serialize a Guid
// when GuidRepresentation is Unspecified." Must run before any Mongo store is constructed.
MongoBsonSetup.EnsureGuidRepresentationRegistered();

var initializeBootstrap = args.Contains("--initialize-bootstrap", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--initialize-bootstrap").ToArray());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// Bootstrap is an explicit deployment mode. It never starts operational Redis/Mongo services.
if (initializeBootstrap || builder.Configuration.GetValue<bool>("Bootstrap:Enabled"))
{
    var signIn = await builder.AddAdminBootstrapAsync(initializeBootstrap);
    if (initializeBootstrap)
    {
        Console.WriteLine("Initialized admin bootstrap state. Existing state is never replaced.");
        return;
    }
    var bootstrapApp = builder.Build();
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
    await bootstrapApp.RunAsync();
    return;
}

// In Development, skip OIDC entirely so UI work isn't blocked on a live Keycloak client.
// Everywhere else every page requires authentication via the global fallback policy below -
// this whole app is admin-only, there's no tiered public/member/maintainer policy to reproduce.
var requireAuth = !builder.Environment.IsDevelopment();

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
            options.Authority = $"{builder.Configuration["Keycloak:Authority"]}/realms/{builder.Configuration["Keycloak:Realm"]}";
            options.ClientId = builder.Configuration["Keycloak:ClientId"];
            options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
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

var app = builder.Build();

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

app.Run();
