using System.Text.Json;
using Aetheric.Provisioning.Engine;
using AethericAdmin.Web.Provisioning;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;
using Xunit;
using Worker = Aetheric.Provisioning.Worker;

namespace AethericAdmin.Tests;

public sealed class PinnedInstitutionDeploymentTests
{
    private static PinnedInstitution Library => PinnedInstitutions.Find("library")!;

    [Fact]
    public async Task Missing_credential_fails_before_publishing_anything()
    {
        var post = new RecordingPostService();
        var deployment = new PinnedInstitutionDeployment(new FakeCredentialStore(), post);

        var ex = await Assert.ThrowsAsync<PinnedInstitutionDeployment.MissingCredentialException>(
            () => deployment.SubmitAsync(Library, default));

        Assert.Contains("mongo", ex.Message);
        Assert.Empty(post.Published);
    }

    [Fact]
    public async Task Library_sends_only_its_mongo_root_credential_for_the_pinned_revision()
    {
        var store = new FakeCredentialStore();
        store.Set("mongo", new("mongo.internal", 27017, "root", "secret") { Mongo = new("admin", true) });
        store.Set("keycloak", new("sso.internal", 8080, "root", "secret") { Keycloak = new("https", "/", "master", "admin-cli") });
        store.Set("s3", new("s3.internal", 9000, "access-key", "secret") { S3 = new("https", true, "us-east-1") });
        var post = new RecordingPostService();

        var requestId = await new PinnedInstitutionDeployment(store, post).SubmitAsync(Library, default);

        var published = Assert.Single(post.Published);
        Assert.Equal("institution/deploy", published.Reference.Address);
        var request = Assert.IsType<InstitutionDeploymentRequested>(published.Message);
        Assert.Equal(requestId, request.RequestId);
        Assert.Equal("https://github.com/aetheric-forge/library", request.Repository);
        Assert.Matches("^[0-9a-f]{40}$", request.Revision);
        Assert.Null(request.Parent);
        var mongo = Assert.Single(request.RootCredentials);
        Assert.Equal("mongo", mongo.Key);
        Assert.Equal("admin", mongo.Value.AuthDatabase);
    }

    [Fact]
    public void Request_and_result_match_the_provisioning_worker_contract()
    {
        Assert.Equal(Worker.ProvisioningPost.RequestReference().Address, InstitutionDeploymentPost.RequestReference().Address);
        Assert.Equal(Worker.ProvisioningPost.RequestReference().Contract.Name, InstitutionDeploymentPost.RequestReference().Contract.Name);
        Assert.Equal(Worker.ProvisioningPost.RequestReference().Contract.Version, InstitutionDeploymentPost.RequestReference().Contract.Version);
        Assert.Equal(Worker.ProvisioningPost.ResultReference().Address, InstitutionDeploymentPost.ResultReference().Address);
        Assert.Equal(Worker.ProvisioningPost.ResultReference().Contract.Name, InstitutionDeploymentPost.ResultReference().Contract.Name);
        Assert.Equal(Worker.ProvisioningPost.ResultReference().Contract.Version, InstitutionDeploymentPost.ResultReference().Contract.Version);

        var request = new InstitutionDeploymentRequested(Guid.NewGuid(), Library.Repository, Library.Revision,
            Library.DefinitionPath, Library.BindingsPath,
            new Dictionary<string, BootstrapRootCredentialPayload> { ["mongo"] = new("mongo.internal", 27017, "root", "secret", AuthDatabase: "admin") },
            null, DateTimeOffset.UtcNow);
        var received = JsonSerializer.Deserialize<Worker.InstitutionDeploymentRequested>(JsonSerializer.Serialize(request))!;
        Assert.Equal(request.RequestId, received.RequestId);
        Assert.Equal(request.Revision, received.Revision);
        Assert.Equal(request.BindingsPath, received.BindingsPath);
        Assert.Null(received.Parent);
        Assert.Equal("admin", received.RootCredentials["mongo"].AuthDatabase);
        Assert.Equal("secret", received.RootCredentials["mongo"].Password);

        var completed = new Worker.InstitutionDeploymentCompleted(request.RequestId, false, ["consumer.delivery_failed"], DateTimeOffset.UtcNow);
        var result = JsonSerializer.Deserialize<InstitutionDeploymentCompleted>(JsonSerializer.Serialize(completed))!;
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal("consumer.delivery_failed", Assert.Single(result.Issues));
    }

    private sealed class FakeCredentialStore : IRootCredentialStore
    {
        private readonly Dictionary<string, RootCredential> _credentials = new(StringComparer.Ordinal);
        public void Set(string system, RootCredential credential) => _credentials[system] = credential;
        public Task SetAsync(string system, RootCredential credential, CancellationToken ct)
        { _credentials[system] = credential; return Task.CompletedTask; }
        public Task<RootCredential?> TryReadAsync(string system, CancellationToken ct) =>
            Task.FromResult(_credentials.TryGetValue(system, out var credential) ? credential : null);
    }

    private sealed class RecordingPostService : IPostService
    {
        public List<(IPostReference Reference, object? Message, IPostMetadata? Metadata)> Published { get; } = [];
        public Task<IPostReference> AcceptAsync(IPostEnvelope envelope, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IPostEnvelope?> CollectAsync(IPostReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task PublishAsync<TMessage>(IPostReference reference, TMessage message, IPostMetadata? metadata = null, CancellationToken ct = default)
        { Published.Add((reference, message, metadata)); return Task.CompletedTask; }
        public Task SubscribeAsync<TMessage>(IPostReference reference, IMessageConsumer<TMessage> consumer, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
