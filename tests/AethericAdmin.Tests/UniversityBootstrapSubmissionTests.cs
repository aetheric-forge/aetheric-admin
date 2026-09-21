using Aetheric.Provisioning.Engine;
using AethericAdmin.Web.Provisioning;
using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class UniversityBootstrapSubmissionTests
{
    private static UniversityEnvelopeDraft Draft() => new UniversityDraft
    {
        UniversityName = "Example University",
        CampusName = "Main Campus",
        FacultyName = "Administration",
        DecisionsName = "Decisions",
        KeycloakAuthority = "https://sso.example.org",
        KeycloakRealm = "example-university",
        KeycloakClientId = "admin-cli",
        RootAdministratorSubjectId = "operator-subject",
    }.Review();

    [Fact]
    public async Task Missing_credential_fails_clearly_before_publishing_anything()
    {
        var store = new FakeCredentialStore();
        var post = new RecordingPostService();
        var submission = new UniversityBootstrapSubmission(store, post);

        var ex = await Assert.ThrowsAsync<UniversityBootstrapSubmission.MissingCredentialException>(
            () => submission.SubmitAsync(Draft(), default));

        Assert.Contains("rabbitmq", ex.Message);
        Assert.Empty(post.Published);
    }

    [Fact]
    public async Task Complete_credentials_publish_a_request_carrying_all_five_resolved_systems()
    {
        var store = new FakeCredentialStore();
        store.Set("rabbitmq", new("rabbitmq.internal", 15672, "root", "secret") { RabbitMq = new() });
        store.Set("mongo", new("mongo.internal", 27017, "root", "secret") { Mongo = new("admin", true) });
        store.Set("keycloak", new("sso.internal", 8080, "root", "secret") { Keycloak = new("https", "/", "master", "admin-cli") });
        store.Set("s3", new("s3.internal", 9000, "access-key", "secret") { S3 = new("https", true, "us-east-1") });
        store.Set("redis", new("redis.internal", 6379, null, "secret"));
        var post = new RecordingPostService();
        var submission = new UniversityBootstrapSubmission(store, post);
        var draft = Draft();

        var requestId = await submission.SubmitAsync(draft, default);

        Assert.Equal(draft.EnvelopeId, requestId);
        var published = Assert.Single(post.Published);
        var request = Assert.IsType<InstitutionBootstrapRequested>(published.Message);
        Assert.Equal(draft.EnvelopeId, request.RequestId);
        Assert.Equal(new HashSet<string> { "rabbitmq", "mongo", "keycloak", "s3", "redis" }, request.RootCredentials.Keys.ToHashSet());
        Assert.Equal("master", request.RootCredentials["keycloak"].Realm);
        Assert.False(request.RootCredentials.ContainsKey("postgres"));
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
