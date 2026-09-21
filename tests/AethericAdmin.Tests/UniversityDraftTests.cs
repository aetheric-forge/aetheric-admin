using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Aetheric.Provisioning.Engine;
using AethericAdmin.Web.Components.Provisioning;
using AethericAdmin.Web.Provisioning;
using AethericForge.Runtime.Abstractions.Interfaces.Post;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Consumers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class UniversityDraftTests
{
    [Fact]
    public void Containment_and_causation_are_distinct_and_dependencies_are_topological()
    {
        var draft = ValidDraft();
        var envelope = draft.Review();
        Assert.Equal("Standard", envelope.Priority);
        Assert.Equal(draft.UniversityId, envelope.InitiatingRequestId);
        Assert.Equal(4, envelope.Requests.Length);
        var university = envelope.Requests[0];
        Assert.Null(university.ParentRequestId);
        Assert.Null(university.CausedByRequestId);
        Assert.Empty(university.DependsOn);
        Assert.Equal(draft.UniversityId, envelope.Requests[1].ParentRequestId);
        Assert.Equal(draft.CampusId, envelope.Requests[2].ParentRequestId);
        Assert.Equal(draft.FacultyId, envelope.Requests[3].ParentRequestId);
        var seen = new HashSet<Guid> { university.RequestId };
        foreach (var request in envelope.Requests.Skip(1))
        {
            Assert.Equal(university.RequestId, request.CausedByRequestId);
            Assert.Equal(request.ParentRequestId, Assert.Single(request.DependsOn));
            Assert.All(request.DependsOn, id => Assert.Contains(id, seen));
            Assert.True(seen.Add(request.RequestId));
        }
    }

    [Fact]
    public void Talent_is_explicitly_rejected_and_four_institution_ids_survive_edits()
    {
        var draft = ValidDraft();
        var original = draft.Review();
        draft.IncludeTalent = true;
        var error = Assert.Throws<ValidationException>(() => draft.Review());
        Assert.Contains("Talent is not supported", error.Message);
        draft.IncludeTalent = false;
        draft.DecisionsName = "Decisions council";
        var edited = draft.Review();
        Assert.Equal(4, edited.Requests.Length);
        Assert.Equal(original.EnvelopeId, edited.EnvelopeId);
        Assert.Equal(original.Requests.Select(x => x.RequestId), edited.Requests.Select(x => x.RequestId));
        Assert.Equal("Decisions", original.Requests[^1].Name);
        Assert.Equal("Decisions council", edited.Requests[^1].Name);
    }

    [Fact]
    public void Review_is_an_immutable_snapshot_with_credential_references_and_unresolved_profiles()
    {
        var draft = ValidDraft();
        var review = draft.Review();
        draft.UniversityName = "Changed";
        Assert.Equal("Aetheric University", review.Requests[0].Name);
        Assert.All(review.Requests, x => Assert.Null(x.PrivateResourceProfileReference));
        Assert.Null(review.Identity.IamDefinitionReference);
        Assert.Equal(new[] { "rabbitmq", "mongo", "keycloak", "s3", "redis" }, review.RootCredentialReferences.Select(x => x.StoreKey));
        var json = JsonSerializer.Serialize(review);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClientSecret", json, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<UniversityEnvelopeDraft>(json)!;
        Assert.Equal(review.EnvelopeId, restored.EnvelopeId);
        Assert.Equal(review.Requests.Select(x => x.RequestId), restored.Requests.Select(x => x.RequestId));
    }

    [Theory]
    [InlineData("http://identity.example")]
    [InlineData("https://user:secret@identity.example")]
    [InlineData("https://identity.example?secret=value")]
    [InlineData("https://identity.example/#fragment")]
    [InlineData("not-an-authority")]
    public void Invalid_identity_authority_cannot_be_reviewed(string authority)
    {
        var draft = ValidDraft();
        draft.KeycloakAuthority = authority;
        Assert.Throws<ValidationException>(() => draft.Review());
    }

    [Fact]
    public void Required_names_and_unsupported_talent_are_validated()
    {
        var draft = ValidDraft();
        draft.UniversityName = " ";
        Assert.Throws<ValidationException>(() => draft.Review());
        draft.UniversityName = "University";
        draft.TalentName = "";
        draft.Review(); // An omitted optional institution need not be configured.
        draft.IncludeTalent = true;
        Assert.Throws<ValidationException>(() => draft.Review());
        draft.TalentName = " decisions ";
        Assert.Throws<ValidationException>(() => draft.Review());
    }

    [Fact]
    public void Separate_operator_sessions_do_not_share_drafts_or_request_ids()
    {
        var first = new UniversityDraftSession();
        var second = new UniversityDraftSession();
        first.Draft.UniversityName = "First";
        Assert.Empty(second.Draft.UniversityName);
        Assert.NotEqual(first.Draft.UniversityId, second.Draft.UniversityId);
    }

    [Fact]
    public async Task Setup_renders_without_live_infrastructure_and_does_not_offer_live_submission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<UniversityDraftSession>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IRootCredentialStore, NoCredentials>();
        services.AddSingleton<IPostService, UnusedPostService>();
        services.AddSingleton<UniversityBootstrapSubmission>();
        services.AddSingleton<BootstrapResultStore>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<UniversitySetup>(ParameterView.Empty)).ToHtmlString());
        Assert.Contains("University name", html);
        Assert.Contains("Campus name", html);
        Assert.Contains("Administration Faculty", html);
        Assert.Contains("Decisions Institution", html);
        Assert.DoesNotContain("Include a Talent Institution", html);
        Assert.Contains("Review envelope", html);
        Assert.DoesNotContain("Download draft envelope", html);
        Assert.Contains("Draft workspace", html);
        Assert.DoesNotContain("Submit to Operations", html); // only appears once a review exists
    }

    private static UniversityDraft ValidDraft() => new()
    {
        UniversityName = "Aetheric University", CampusName = "Main Campus",
        KeycloakAuthority = "https://identity.example", KeycloakRealm = "forge",
        KeycloakClientId = "admin", RootAdministratorSubjectId = "root-subject"
    };

    [Fact]
    public async Task Editing_a_child_form_removes_stale_review_and_refreshes_the_hierarchy()
    {
        var session = new UniversityDraftSession();
        session.Draft.UniversityName = "University";
        session.Draft.CampusName = "Campus";
        session.Draft.KeycloakAuthority = "https://identity.example";
        session.Draft.KeycloakRealm = "forge";
        session.Draft.KeycloakClientId = "admin";
        session.Draft.RootAdministratorSubjectId = "root-subject";
        var activator = new CapturingActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(session);
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IRootCredentialStore, NoCredentials>();
        services.AddSingleton<IPostService, UnusedPostService>();
        services.AddSingleton<UniversityBootstrapSubmission>();
        services.AddSingleton<BootstrapResultStore>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<UniversitySetup>(ParameterView.Empty);
            var form = Assert.IsType<EditForm>(activator.Form);
            Assert.True(form.EditContext!.Validate());
            await form.OnValidSubmit.InvokeAsync(form.EditContext);
            Assert.Contains("Download draft envelope", component.ToHtmlString());
            Assert.Contains("Not queued", component.ToHtmlString());
            session.Draft.FacultyName = "Regency";
            form.EditContext.NotifyFieldChanged(new FieldIdentifier(session.Draft, nameof(UniversityDraft.FacultyName)));
            var edited = component.ToHtmlString();
            Assert.DoesNotContain("Download draft envelope", edited);
            Assert.DoesNotContain("Inspect draft JSON", edited);
            Assert.Contains("<li>Regency", edited);
        });
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public EditForm? Form { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is EditForm form) Form = form;
            return component;
        }
    }

    // These two rendering tests never click "Submit to Operations" - UniversitySetup.razor just
    // needs something to inject; neither is ever called.
    private sealed class NoCredentials : IRootCredentialStore
    {
        public Task SetAsync(string system, RootCredential credential, CancellationToken ct) => throw new NotSupportedException();
        public Task<RootCredential?> TryReadAsync(string system, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class UnusedPostService : IPostService
    {
        public Task<IPostReference> AcceptAsync(IPostEnvelope envelope, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IPostEnvelope?> CollectAsync(IPostReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task PublishAsync<TMessage>(IPostReference reference, TMessage message, IPostMetadata? metadata = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task SubscribeAsync<TMessage>(IPostReference reference, IMessageConsumer<TMessage> consumer, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
