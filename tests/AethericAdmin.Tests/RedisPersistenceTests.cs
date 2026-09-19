using System.Diagnostics;
using System.Text.Json;
using AethericAdmin.Web.Hosting;
using AethericAdmin.Web.Infrastructure;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Maintenance.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace AethericAdmin.Tests;

[Trait("Category", "RedisIntegration")]
public sealed class RedisPersistenceTests : IAsyncLifetime
{
    private readonly string _prefix = "aetheric-admin-test:" + Guid.NewGuid().ToString("N") + ":";
    private ConnectionMultiplexer _redis = null!;
    public async Task InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(
        RedisPersistenceExtensions.ConnectionOptions(TestHost.Configuration(_prefix), "Workbench"));

    [Fact]
    public async Task Fresh_process_reads_history_and_decrypts_credentials_created_by_previous_process()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await RunProbe("write", path);
            await RunProbe("read", path);
            Assert.NotEmpty(await _redis.GetDatabase().ListRangeAsync(_prefix + "keys"));
            foreach (var key in Keys()) Assert.Null(await _redis.GetDatabase().KeyTimeToLiveAsync(key));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Workbench_roundtrips_separates_types_and_notifies_local_subscribers()
    {
        var workbench = new RedisWorkbenchService(_redis.GetDatabase(), _prefix + "workbench:");
        var calls = 0;
        using (workbench.Subscribe<string>((value, ct) => { calls++; return Task.CompletedTask; }))
        {
            await workbench.PutAsync("same", "value");
            await workbench.PutAsync("same", 42);
            Assert.Equal("value", await workbench.GetAsync<string>("same"));
            Assert.Equal(42, await workbench.GetAsync<int>("same"));
        }
        await workbench.PutAsync("same", "updated");
        Assert.Equal(1, calls);
        Assert.Null(await workbench.GetAsync<string>("missing"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workbench.PutAsync("same", "cancelled", cancelled.Token));
        Assert.Equal("updated", await workbench.GetAsync<string>("same"));
    }

    [Fact]
    public async Task Corrupt_history_is_not_treated_as_an_empty_ledger()
    {
        await using var services = TestHost.Create(_prefix);
        var caretaker = services.GetRequiredService<ICaretaker>();
        var command = new MaintenanceCommand(Guid.NewGuid(), "test", "job", DateTimeOffset.UtcNow, "test");
        await caretaker.PostAsync("test", command);
        var key = Assert.Single(Keys());
        await _redis.GetDatabase().StringSetAsync(key, "broken JSON");
        await Assert.ThrowsAsync<JsonException>(() => caretaker.PostAsync("test", command with { Id = Guid.NewGuid() }));
        Assert.Equal("broken JSON", (string?)await _redis.GetDatabase().StringGetAsync(key));
    }

    [Fact]
    public void Invalid_port_is_rejected_before_connecting()
    {
        var config = new ConfigurationBuilder().AddConfiguration(TestHost.Configuration(_prefix))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Workbench:Redis:Password"] = "incorrect", ["Redis:Port"] = "0" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAdminRedisPersistence(config));
    }

    [Fact]
    public void Invalid_credentials_fail_instead_of_falling_back_to_process_storage()
    {
        var config = new ConfigurationBuilder().AddConfiguration(TestHost.Configuration(_prefix))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Workbench:Redis:Password"] = "incorrect" }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAdminRedisPersistence(config);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<RedisConnectionException>(() => provider.GetRequiredService<RedisWorkbenchService>());
    }

    [Fact]
    public async Task Data_protection_application_names_isolate_payloads()
    {
        await using var first = TestHost.Create(_prefix);
        var payload = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("value");
        var config = new ConfigurationBuilder().AddConfiguration(TestHost.Configuration(_prefix))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:ApplicationName"] = "AnotherApp" }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAdminRedisPersistence(config);
        using var second = services.BuildServiceProvider();
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(payload));
        await Task.CompletedTask;
    }

    private RedisKey[] Keys() => _redis.GetServer(_redis.GetEndPoints()[0]).Keys(pattern: _prefix + "*").ToArray();

    private async Task RunProbe(string phase, string path)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { typeof(RestartProbe).Assembly.Location, phase, _prefix, path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    public async Task DisposeAsync()
    {
        if (_redis is null) return;
        foreach (var key in Keys()) await _redis.GetDatabase().KeyDeleteAsync(key);
        await _redis.DisposeAsync();
    }
}
