using AethericAdmin.Web.Components;
using AethericAdmin.Web.Hosting;
using AethericAdmin.Web.Maintenance;
using AethericAdmin.Web.Maintenance.Jobs;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

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

builder.Services.AddForgeCampus();

builder.Services.AddSingleton(TimeProvider.System);

// Maintenance owns its job and encrypted-credential stores - a keyed client keeps this
// connection separate should other institutions ever join this app.
var maintenanceMongoUrl = MongoUrl.Create(
    ForgeCampusExtensions.BuildMongoUri(
        InstitutionServiceConfiguration.Resolve(builder.Configuration, "Maintenance", "MongoDb")));
builder.Services.AddKeyedSingleton<IMongoClient>(
    "Maintenance",
    (_, _) => new MongoClient(maintenanceMongoUrl));
builder.Services.AddKeyedSingleton<IMongoDatabase>(
    "Maintenance",
    (sp, _) => sp.GetRequiredKeyedService<IMongoClient>("Maintenance").GetDatabase(maintenanceMongoUrl.DatabaseName));

builder.Services.AddSingleton<ICredentialStore, MongoCredentialStore>();
builder.Services.AddSingleton<IJobDefinitionStore, MongoJobDefinitionStore>();
builder.Services.AddSingleton<IJobExecutor, SshJobExecutor>();
builder.Services.AddSingleton<IJobExecutor, CodeJobExecutor>();
builder.Services.AddSingleton<JobDispatcher>();
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
