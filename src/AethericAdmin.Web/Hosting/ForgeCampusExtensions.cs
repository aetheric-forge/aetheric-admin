using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Composition;
using AethericForge.Runtime.Institutions.Abstractions.Models;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.Campus;
using AethericForge.Runtime.Institutions.Maintenance;
using AethericForge.Runtime.Institutions.PostOffice;
using AethericForge.Runtime.Institutions.Workbench;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Post.RabbitMq;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Services.Maintenance;
using AethericForge.Runtime.Services.Post;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;
using AethericAdmin.Web.Infrastructure;
using AethericAdmin.Web.Provisioning;

namespace AethericAdmin.Web.Hosting;

/// <summary>
/// Minimal Campus: Redis-backed Workbench and Operations -> Maintenance. Caretaker's
/// ledger survives host restarts, but its read/modify/write gate is still process-local;
/// only one active admin instance is supported. Unmounted Campus services remain lazy.
/// </summary>
public static class ForgeCampusExtensions
{
    // Admin has no scoped RabbitMQ credentials of its own - the provisioner is what creates
    // scoped resources, and Post Office is how admin reaches it in the first place. This uses
    // the platform-admin root RabbitMQ user directly (configured here, not read back out of
    // IRootCredentialStore - that store's RootCredential shape targets the HTTP management API,
    // not the AMQP port/vhost this connection needs).
    internal static string BuildRabbitMqUrl(IConfiguration configuration)
    {
        var useSsl = configuration.GetValue("RabbitMq:Ssl", false);
        var builder = new UriBuilder
        {
            Scheme = useSsl ? "amqps" : "amqp",
            Host = GetRequiredSetting(configuration, "RabbitMq:Host"),
            Port = configuration.GetValue<int?>("RabbitMq:Port") ?? (useSsl ? 5671 : 5672),
            UserName = GetRequiredSetting(configuration, "RabbitMq:Username"),
            Password = GetRequiredSetting(configuration, "RabbitMq:Password"),
            Path = Uri.EscapeDataString(GetRequiredSetting(configuration, "RabbitMq:VirtualHost"))
        };

        return builder.Uri.ToString();
    }

    private static string GetRequiredSetting(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required.");

    public static IServiceCollection AddForgeCampus(this IServiceCollection services)
    {
        services.AddInstitutionTemplate(builder =>
        {
            builder.WithDescriptor(
                    "AethericAdmin",
                    new Version(1, 0, 0),
                    "The Aetheric Forge back-office admin app.")
                .With<ITeam<IMaintenanceClerk>>(_ => new Team<IMaintenanceClerk>(Array.Empty<IMaintenanceClerk>()))
                .With<ICaretaker, Caretaker>()
                .With<IStagingProvider>(_ => new InMemoryStagingProvider("Default"))
                .With<IStagingService, StagingService>()
                .With<IWorkbenchService>(sp => sp.GetRequiredService<RedisWorkbenchService>())
                .With<ITeam<IWorkbenchWorker>>(_ => new Team<IWorkbenchWorker>(Array.Empty<IWorkbenchWorker>()))
                .With<IArtificer, Artificer>()
                .With<IWorkbenchContext, WorkbenchContext>()
                .With<IWorkbench, Workbench>()
                .With<IPostProvider>(sp => new RabbitMqPostProvider(
                    ProvisioningPost.Domain,
                    BuildRabbitMqUrl(sp.GetRequiredService<IConfiguration>())))
                .With<IPostService, PostService>()
                .With<ITeam<IPostClerk>>(_ => new Team<IPostClerk>(Array.Empty<IPostClerk>()))
                .With<IPostExchange, PostExchange>()
                .With<IPostmaster, Postmaster>()
                .With<IPostOfficeContext, PostOfficeContext>()
                .With<IPostOffice, PostOffice>();
        });

        services.AddSingleton<ICampus>(serviceProvider =>
        {
            var campusTemplate = (InstitutionTemplate)serviceProvider.GetRequiredService<IInstitutionTemplate>();
            var campusContext = new CampusContext(campusTemplate, serviceProvider);
            var campus = new Campus(campusContext);

            campus.RegisterInstitution<IWorkbench, Workbench, WorkbenchContext>(
                campusTemplate, serviceProvider, "Workbench",
                static (template, sp, parent) => new WorkbenchContext(template, sp, parent));

            campus.RegisterInstitution<IPostOffice, PostOffice, PostOfficeContext>(
                campusTemplate, serviceProvider, "PostOffice",
                static (template, sp, parent) => new PostOfficeContext(template, sp, parent));

            var operations = campus.RegisterFaculty<IOperationsFaculty>(
                campusTemplate, serviceProvider, "Operations", "Quartermaster",
                static (context, dean) => new OperationsFaculty(context, dean));

            var maintenanceTemplate = campusTemplate with
            {
                Descriptor = new InstitutionDescriptor("Maintenance", campusTemplate.Descriptor.Version, "Maintenance institution")
            };
            var maintenanceContext = new MaintenanceContext(maintenanceTemplate, serviceProvider, operations);
            operations.Register<IMaintenance>(
                ActivatorUtilities.CreateInstance<global::AethericForge.Runtime.Institutions.Maintenance.Maintenance>(
                    serviceProvider,
                    maintenanceContext));

            return campus;
        });

        var deploymentResultStore = new CampusDeploymentResultStore();
        services.AddSingleton(deploymentResultStore);
        services.AddPostSubscription(
            ProvisioningPost.ResultReference(),
            new CampusDeploymentResultConsumer(deploymentResultStore));

        var bootstrapResultStore = new BootstrapResultStore();
        services.AddSingleton(bootstrapResultStore);
        services.AddPostSubscription(
            ProvisioningBootstrapPost.ResultReference(),
            new BootstrapResultConsumer(bootstrapResultStore));
        services.AddSingleton<UniversityBootstrapSubmission>();

        return services;
    }
}
