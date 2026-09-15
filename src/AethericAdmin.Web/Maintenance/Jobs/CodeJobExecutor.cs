using System.Reflection;
using System.Runtime.Loader;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;

namespace AethericAdmin.Web.Maintenance.Jobs;

/// <summary>
/// Loads the job's assembly into its own collectible AssemblyLoadContext and invokes the configured
/// static method, then unloads the context. Runs in-process with the same privileges as the web app -
/// no sandboxing - an accepted tradeoff since this is already gated behind the ForgeAdministrator
/// policy. Method convention: `public static Task&lt;int&gt; MethodName(CancellationToken)`, returning
/// a process-style exit code (0 = success).
/// </summary>
public sealed class CodeJobExecutor : IJobExecutor
{
    public JobDefinitionKind Kind => JobDefinitionKind.Code;

    public async Task<MaintenanceRunOutcome> RunAsync(
        JobDefinition job,
        MaintenanceCommand command,
        string? passphrase,
        CancellationToken ct)
    {
        var config = job.Code ?? throw new InvalidOperationException($"Job '{job.Name}' has no Code configuration.");
        var context = new AssemblyLoadContext($"maintenance-job-{job.Id:N}", isCollectible: true);

        try
        {
            if (!File.Exists(config.AssemblyPath))
            {
                throw new FileNotFoundException($"Assembly not found at '{config.AssemblyPath}'.", config.AssemblyPath);
            }

            var assembly = context.LoadFromAssemblyPath(config.AssemblyPath);
            var type = assembly.GetType(config.TypeName)
                ?? throw new InvalidOperationException($"Type '{config.TypeName}' was not found in '{config.AssemblyPath}'.");
            var method = type.GetMethod(config.MethodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Static method '{config.MethodName}' was not found on '{config.TypeName}'.");

            var invocationResult = method.Invoke(null, [ct]);
            var exitCode = invocationResult switch
            {
                Task<int> intTask => await intTask.ConfigureAwait(false),
                Task task => await AwaitAndReturnZeroAsync(task).ConfigureAwait(false),
                int result => result,
                _ => throw new InvalidOperationException(
                    $"'{config.MethodName}' must return Task<int>, Task, or int.")
            };

            var status = exitCode == 0 ? MaintenanceRunStatus.Completed : MaintenanceRunStatus.Failed;
            return new MaintenanceRunOutcome(
                command.Id,
                status,
                ProcessedCount: 1,
                RemainingCount: 0,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                FailureReason: status == MaintenanceRunStatus.Failed ? $"Exit code {exitCode}" : null);
        }
        catch (Exception exception)
        {
            var actual = exception is TargetInvocationException { InnerException: not null } wrapped
                ? wrapped.InnerException
                : exception;
            return new MaintenanceRunOutcome(
                command.Id,
                MaintenanceRunStatus.Failed,
                ProcessedCount: 0,
                RemainingCount: 0,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                FailureReason: actual!.Message);
        }
        finally
        {
            context.Unload();
        }
    }

    private static async Task<int> AwaitAndReturnZeroAsync(Task task)
    {
        await task.ConfigureAwait(false);
        return 0;
    }
}
