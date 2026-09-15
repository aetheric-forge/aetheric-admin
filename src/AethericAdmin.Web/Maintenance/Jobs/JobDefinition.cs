namespace AethericAdmin.Web.Maintenance.Jobs;

public sealed record JobDefinition(
    Guid Id,
    string Name,
    JobDefinitionKind Kind,
    string? CronSchedule,
    bool Enabled,
    SshJobConfig? Ssh,
    CodeJobConfig? Code,
    DateTimeOffset CreatedAt);

public enum JobDefinitionKind
{
    Ssh,
    Code
}

public sealed record SshJobConfig(Guid CredentialId, string Host, int Port, string Command);

public sealed record CodeJobConfig(string AssemblyPath, string TypeName, string MethodName);
