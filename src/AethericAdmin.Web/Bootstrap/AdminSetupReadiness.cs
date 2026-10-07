using Aetheric.Provisioning.Engine;

namespace AethericAdmin.Web.Bootstrap;

public static class AdminSetupReadiness
{
    public static IReadOnlyList<string> RequiredSystems { get; } = Array.AsReadOnly(new[] { "rabbitmq", "mongo", "keycloak", "s3", "redis" });
    public static async Task<bool> HasRootCredentialsAsync(IRootCredentialStore store, CancellationToken ct = default)
    {
        foreach (var system in RequiredSystems)
            if (await store.TryReadAsync(system, ct) is null) return false;
        return true;
    }
}
