using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Persistence;

namespace AethericAdmin.Web.Bootstrap;

public static class OperationalClientSecret
{
    public static async Task<bool> ApplyAsync(IConfiguration configuration, IRootCredentialStore store, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Keycloak:ClientSecret"])) return true;
        var credential = await store.TryReadAsync("provisioner-client", ct);
        if (credential is null) return false;
        var authority = configuration["Keycloak:Authority"] ?? "";
        var issuer = authority.Contains("/realms/", StringComparison.Ordinal)
            ? authority : $"{authority.TrimEnd('/')}/realms/{Uri.EscapeDataString(configuration["Keycloak:Realm"] ?? "")}";
        // Never send a persisted secret to a different issuer or client.
        if (!string.Equals(credential.Host.TrimEnd('/'), issuer.TrimEnd('/'), StringComparison.Ordinal)
            || credential.Username != configuration["Keycloak:ClientId"]) return false;
        configuration["Keycloak:ClientSecret"] = credential.Password;
        configuration["Keycloak:CallbackPath"] ??= "/setup/signin-oidc";
        return true;
    }
}
