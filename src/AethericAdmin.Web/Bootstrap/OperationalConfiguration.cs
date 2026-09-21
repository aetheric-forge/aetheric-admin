using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Persistence;

namespace AethericAdmin.Web.Bootstrap;

/// <summary>
/// Derives normal-mode operational configuration (Keycloak OIDC login, RabbitMq, Redis, and
/// admin's own Maintenance/Membership Mongo connections) from state the one-time bootstrap flow
/// already collected, instead of requiring every one of those to be hand-set via appsettings/env
/// vars. Explicit configuration always wins per key - this only fills in what's genuinely missing,
/// so nothing about normal-mode startup becomes less flexible than it is today.
///
/// Two sources, both already durable and already shared with the bootstrap container via the same
/// volumes: RegistryBootstrapState (the OIDC client the bootstrapping administrator signed in
/// with - reused directly for ongoing admin login, since there has only ever been the one client)
/// and the root credentials the Infrastructure setup wizard collected (redis/rabbitmq/mongo -
/// admin already connects to its own backing infrastructure as that same root/platform-admin user
/// today, per ForgeCampusExtensions' own RabbitMq comment; this just stops requiring the operator
/// to retype that same credential a second time by hand).
/// </summary>
public static class OperationalConfiguration
{
    public static async Task ApplyDerivedDefaultsAsync(this WebApplicationBuilder builder, CancellationToken ct = default)
    {
        var configuration = builder.Configuration;
        var derived = new Dictionary<string, string?>(StringComparer.Ordinal);

        RegistryBootstrapState? bootstrapState = null;
        async Task<RegistryBootstrapState> RequireBootstrapCompletedAsync(string reason)
        {
            if (bootstrapState is not null) return bootstrapState;
            var store = new FileRegistryBootstrapStore(
                configuration["BootstrapConnection:StateDirectory"] ?? "data/bootstrap");
            RegistryBootstrapState state;
            try { state = await store.ReadAsync(ct); }
            catch (FileNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Bootstrap has not been run yet, so {reason} cannot be derived. " +
                    "Run this deployment once with --initialize-bootstrap and complete the setup wizard first.");
            }
            if (state.Phase != RegistryBootstrapPhase.Completed)
                throw new InvalidOperationException(
                    $"Bootstrap is not complete yet, so {reason} cannot be derived. " +
                    "Finish the infrastructure setup wizard first.");
            return bootstrapState = state;
        }

        IRootCredentialStore? credentialStore = null;
        async Task<RootCredential> RequireCredentialAsync(string system)
        {
            credentialStore ??= new ManagedRootCredentialStore(
                configuration["RootCredentials:Directory"] ?? "data/root-credentials",
                configuration["RootCredentials:KeyDirectory"] ?? "data/root-key");
            return await credentialStore.TryReadAsync(system, ct) ?? throw new InvalidOperationException(
                $"No '{system}' root credential is stored. Add it through the infrastructure setup wizard first.");
        }

        if (string.IsNullOrWhiteSpace(configuration["Keycloak:Authority"]))
        {
            var state = await RequireBootstrapCompletedAsync("Keycloak login configuration");
            // Already the full issuer URL (Authority + "/realms/" + Realm) - Program.cs's own
            // OIDC setup detects this shape and uses it as-is rather than re-appending a realm.
            derived["Keycloak:Authority"] = state.Settings.Issuer;
            derived["Keycloak:ClientId"] = state.Settings.ClientId;
        }

        if (string.IsNullOrWhiteSpace(configuration["RabbitMq:Host"]))
        {
            var credential = await RequireCredentialAsync("rabbitmq");
            derived["RabbitMq:Host"] = credential.Host;
            derived["RabbitMq:Username"] = credential.Username;
            derived["RabbitMq:Password"] = credential.Password;
            derived["RabbitMq:Ssl"] = (credential.RabbitMq?.Scheme == "https").ToString();
            // The stored credential's own Port is the management API port; AMQP uses the
            // standard 5672/5671 default BuildRabbitMqUrl already falls back to when Port is
            // absent - leave it unset rather than duplicating that port-selection logic here.
            derived["RabbitMq:VirtualHost"] = "/";
        }

        if (string.IsNullOrWhiteSpace(configuration["Redis:Host"]))
        {
            var credential = await RequireCredentialAsync("redis");
            derived["Redis:Host"] = credential.Host;
            derived["Redis:Port"] = credential.Port.ToString();
            derived["Redis:Ssl"] = "false";
            derived["Redis:Database"] = "0";
            derived["Workbench:Redis:User"] = credential.Username ?? "";
            derived["Workbench:Redis:Password"] = credential.Password;
            derived["ForgeCampus:Redis:User"] = credential.Username ?? "";
            derived["ForgeCampus:Redis:Password"] = credential.Password;
        }

        if (string.IsNullOrWhiteSpace(configuration["Maintenance:MongoDb:Host"]))
        {
            var credential = await RequireCredentialAsync("mongo");
            ApplyMongo(derived, "Maintenance", credential, "aetheric-admin");
        }
        if (string.IsNullOrWhiteSpace(configuration["Membership:MongoDb:Host"]))
        {
            var credential = await RequireCredentialAsync("mongo");
            ApplyMongo(derived, "Membership", credential, "aetheric-membership");
        }

        // Appended (highest priority), not inserted first: appsettings.json already declares
        // these keys as explicit empty strings for documentation, which - being present, not
        // absent - would still win over a lower-priority fallback. Safe to override here because
        // every key in `derived` was only added after its own IsNullOrWhiteSpace check above, so
        // this can never clobber a value the operator actually set.
        if (derived.Count > 0)
            configuration.AddInMemoryCollection(derived);
    }

    private static void ApplyMongo(Dictionary<string, string?> derived, string institution, RootCredential credential, string databaseName)
    {
        derived[$"{institution}:MongoDb:Host"] = credential.Host;
        derived[$"{institution}:MongoDb:Port"] = credential.Port.ToString();
        derived[$"{institution}:MongoDb:Username"] = credential.Username;
        derived[$"{institution}:MongoDb:Password"] = credential.Password;
        derived[$"{institution}:MongoDb:DatabaseName"] = databaseName;
        derived[$"{institution}:MongoDb:AuthenticationDatabase"] = credential.Mongo?.AuthDatabase ?? "admin";
    }
}
