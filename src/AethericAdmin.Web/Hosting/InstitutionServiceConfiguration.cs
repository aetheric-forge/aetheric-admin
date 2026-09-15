namespace AethericAdmin.Web.Hosting;

/// <summary>Resolves service settings without inheriting credentials across institutions.</summary>
public static class InstitutionServiceConfiguration
{
    private static readonly Dictionary<string, string[]> Credentials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Keycloak"] = ["ClientId", "ClientSecret"],
        ["MongoDb"] = ["Username", "Password"],
        ["RabbitMq"] = ["Username", "Password"],
        ["S3"] = ["AccessKey", "SecretKey"],
        ["Redis"] = ["User", "Password"]
    };

    private static readonly Dictionary<string, string[]> Settings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Keycloak"] = ["Authority", "Realm", "AdminApiBaseAddress", "DirectoryFreshnessLifetime"],
        ["MongoDb"] = ["Host", "Port", "DatabaseName", "AuthenticationDatabase", "AuthenticationMechanism", "DirectConnection"],
        ["RabbitMq"] = ["Host", "Port", "Ssl", "VirtualHost"],
        ["S3"] = ["ServiceUrl", "ForcePathStyle", "AuthenticationRegion", "BucketName"],
        ["Redis"] = ["Host", "Port", "Ssl", "Database"]
    };

    public static IConfiguration Resolve(IConfiguration configuration, string institution, string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institution);
        if (institution.Contains(':'))
            throw new ArgumentException("Institution must be a single configuration segment.", nameof(institution));
        if (!Credentials.TryGetValue(service, out var credentials))
            throw new ArgumentException("Unsupported infrastructure service.", nameof(service));

        var platform = configuration.GetSection(service);
        var local = configuration.GetSection($"{institution}:{service}");
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in platform.GetChildren().Concat(local.GetChildren()))
        {
            // Only scalar settings are supported. Never copy opaque connection strings or nested secrets.
            if (field.GetChildren().Any() || !Settings[service].Concat(credentials).Contains(field.Key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported configuration setting '{field.Path}'.");
            values[$"{service}:{field.Key}"] = local.GetChildren().Any(child => child.Key.Equals(field.Key, StringComparison.OrdinalIgnoreCase))
                ? local[field.Key] : platform[field.Key];
        }

        foreach (var field in new[] { "Authority", "AdminApiBaseAddress", "ServiceUrl", "Host" })
        {
            var value = values.GetValueOrDefault($"{service}:{field}");
            if (value is not null && (value.Contains('@') ||
                (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))))
                throw new InvalidOperationException($"Configuration setting '{service}:{field}' must be an endpoint without credentials, query, or fragment.");
        }

        foreach (var credential in credentials)
        {
            var value = local[credential];
            // Redis ACL user is optional; the password is always institution-owned.
            if (string.IsNullOrWhiteSpace(value) && !(service.Equals("Redis", StringComparison.OrdinalIgnoreCase) && credential == "User"))
                throw new InvalidOperationException($"Configuration value '{local.Path}:{credential}' is required by institution credential policy.");
            values[$"{service}:{credential}"] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
