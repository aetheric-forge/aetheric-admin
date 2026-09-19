using Aetheric.Provisioning.Application;
using Aetheric.Provisioning.Engine;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;

namespace AethericAdmin.Web.Provisioning;

/// <summary>
/// The operator-facing surface for the first Post Office message: a one-shot trigger, not a
/// scheduled JobDispatcher job (this is a single deliberate action, not recurring maintenance).
/// Today's expected outcome is a CampusDeploymentCompleted reporting provider.unsupported for
/// every resource except Workbench - that's correct until real resource providers exist.
/// </summary>
public static class CampusDeploymentEndpoints
{
    public static IEndpointRouteBuilder MapCampusDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/provisioning/deploy-template-campus", async (
            IConfiguration configuration,
            IRootCredentialStore credentialStore,
            IPostService postService,
            CancellationToken ct) =>
        {
            var credentials = new Dictionary<string, RootCredentialPayload>(StringComparer.Ordinal);
            foreach (var system in InfrastructureConnections.Systems)
            {
                var credential = await credentialStore.TryReadAsync(system, ct);
                if (credential is null)
                {
                    return Results.Problem(
                        $"No root credential is stored for '{system}'. Complete infrastructure bootstrap first.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                credentials[system] = ToPayload(credential);
            }

            var request = new CampusDeploymentRequested(
                RequestId: Guid.NewGuid(),
                Repository: RequiredSetting(configuration, "TemplateCampus:Repository"),
                Revision: RequiredSetting(configuration, "TemplateCampus:Revision"),
                DefinitionPath: configuration["TemplateCampus:DefinitionPath"] ?? "institution/campus.yaml",
                BindingsPath: configuration["TemplateCampus:BindingsPath"] ?? "institution/campus.bindings.yaml",
                RootCredentials: credentials,
                RequestedAtUtc: DateTimeOffset.UtcNow);

            await postService.PublishAsync(ProvisioningPost.RequestReference(), request, ct: ct);

            return Results.Accepted(value: new { request.RequestId });
        });

        endpoints.MapGet("/admin/provisioning/deploy-template-campus/{requestId:guid}", (
            Guid requestId,
            CampusDeploymentResultStore resultStore) =>
        {
            var result = resultStore.TryGet(requestId);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

        return endpoints;
    }

    private static RootCredentialPayload ToPayload(RootCredential credential) => new(
        Host: credential.Host,
        Port: credential.Port,
        Username: credential.Username,
        Password: credential.Password,
        AuthDatabase: credential.Mongo?.AuthDatabase,
        Database: credential.Postgres?.Database,
        Scheme: credential.RabbitMq?.Scheme,
        BasePath: credential.RabbitMq?.BasePath);

    private static string RequiredSetting(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required.");
}
