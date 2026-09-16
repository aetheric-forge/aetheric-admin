using AethericAdmin.Web.Hosting;
using AethericAdmin.Web.Maintenance.Jobs;
using AethericForge.Runtime.Institutions.Campus;
using AethericForge.Runtime.Institutions.Maintenance;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace AethericAdmin.Tests;

public static class RestartProbe
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3) return 2;
        using var services = TestHost.Create(args[1]);
        var caretaker = services.GetRequiredService<ICampus>().Resolve<IOperationsFaculty>().Resolve<IMaintenance>().Caretaker;
        var protector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("AethericAdmin.Web.Maintenance.SshCredentials");
        const string domain = JobDispatcher.Domain;
        if (args[0] == "write")
        {
            var command = new MaintenanceCommand(Guid.NewGuid(), domain, "restart-test", DateTimeOffset.UtcNow, "test");
            await caretaker.PostAsync(domain, command);
            await caretaker.CollectNextAsync(domain, command.Job);
            await caretaker.RecordOutcomeAsync(domain, new(command.Id, MaintenanceRunStatus.Completed, 1, 0, DateTimeOffset.UtcNow));
            await File.WriteAllTextAsync(args[2], protector.Protect("synthetic-ssh-credential"));
            return 0;
        }
        var runs = await caretaker.ListRunsAsync(domain);
        return runs.Count == 1 && runs[0].IsCollected && runs[0].Outcome?.Status == MaintenanceRunStatus.Completed
            && protector.Unprotect(await File.ReadAllTextAsync(args[2])) == "synthetic-ssh-credential" ? 0 : 1;
    }
}
