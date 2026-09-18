using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Faculty.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Composition;
using AethericForge.Runtime.Institutions.Abstractions.Models;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.Campus;
using AethericForge.Runtime.Institutions.Faculty;
using AethericForge.Runtime.Institutions.Maintenance;
using AethericForge.Runtime.Institutions.Workbench;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Services.Faculty;
using AethericForge.Runtime.Services.Maintenance;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;
using AethericAdmin.Web.Infrastructure;

namespace AethericAdmin.Web.Hosting;

/// <summary>
/// Minimal Campus: Redis-backed Workbench and Operations -> Maintenance. Caretaker's
/// ledger survives host restarts, but its read/modify/write gate is still process-local;
/// only one active admin instance is supported. Unmounted Campus services remain lazy.
/// </summary>
public static class ForgeCampusExtensions
{
    private static TFaculty RegisterFaculty<TFaculty>(
        Campus campus,
        InstitutionTemplate campusTemplate,
        IServiceProvider serviceProvider,
        string name,
        string deanTitle,
        Func<IFacultyContext, IDean, TFaculty> factory)
        where TFaculty : class, IFaculty
    {
        var template = campusTemplate with
        {
            Descriptor = new InstitutionDescriptor(name, campusTemplate.Descriptor.Version, $"{name} faculty")
        };
        var context = new FacultyContext(template, serviceProvider, campus);
        var dean = new Dean(deanTitle, new Team<IFacultyClerk>(Array.Empty<IFacultyClerk>()));
        var faculty = factory(context, dean);
        campus.Register<TFaculty>(faculty);
        return faculty;
    }

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
                .With<IWorkbench, Workbench>();
        });

        services.AddSingleton<ICampus>(serviceProvider =>
        {
            var campusTemplate = (InstitutionTemplate)serviceProvider.GetRequiredService<IInstitutionTemplate>();
            var campusContext = new CampusContext(campusTemplate, serviceProvider);
            var campus = new Campus(campusContext);

            var workbenchTemplate = campusTemplate with
            {
                Descriptor = new InstitutionDescriptor("Workbench", campusTemplate.Descriptor.Version, "Workbench institution")
            };
            campus.Register<IWorkbench>(ActivatorUtilities.CreateInstance<Workbench>(
                serviceProvider,
                new WorkbenchContext(workbenchTemplate, serviceProvider, campus)));

            var operations = RegisterFaculty<IOperationsFaculty>(
                campus, campusTemplate, serviceProvider, "Operations", "Quartermaster",
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

        return services;
    }
}
