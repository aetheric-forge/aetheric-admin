using System.Text.Json;
using System.Text.Json.Nodes;
using AethericAdmin.Web.Provisioning;
using AethericForge.Runtime.Models.Post;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class BootstrapContractTests
{
    private static JsonNode Fixture(string name) => JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "BootstrapContractFixtures", name + ".json")))!;

    private static void EqualJson(JsonNode expected, object actual) =>
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(actual)),
            "Wire JSON differs from the canonical runtime fixture.");

    // Metadata is an AMQP header/property projection, not a JSON-deserialized PostMetadata body.
    private static PostMetadata ReadMetadata(JsonNode node) => new(
        messageId: node["MessageId"]!.GetValue<string>(),
        correlationId: node["CorrelationId"]?.GetValue<string>(),
        causationId: node["CausationId"]?.GetValue<string>(),
        producedAtUtc: node["ProducedAtUtc"]!.GetValue<DateTimeOffset>(),
        attributes: node["Attributes"]!.Deserialize<Dictionary<string, string>>());

    [Fact]
    public void Request_payload_and_routing_match_canonical_runtime_fixture()
    {
        var fixture = Fixture("request");
        var request = fixture["Payload"]!.Deserialize<InstitutionBootstrapRequested>()!;
        EqualJson(fixture["Payload"]!, request);
        EqualJson(fixture["Reference"]!, ProvisioningBootstrapPost.RequestReference());
        Assert.Equal(Guid.Parse(fixture["Metadata"]!["MessageId"]!.GetValue<string>()), request.RequestId);
        Assert.Equal(new[] { "keycloak", "mongo", "rabbitmq", "s3" }, request.RootCredentials.Keys.Order());
        Assert.Equal("master", request.RootCredentials["keycloak"].Realm);
        Assert.Contains("\n", request.Decisions.DefinitionYaml);
        Assert.NotEqual(request.Decisions.DefinitionYaml, request.Decisions.BindingsYaml);
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    public void Completion_payload_preserves_numeric_statuses_and_order(string name, bool succeeded)
    {
        var fixture = Fixture(name);
        var result = fixture["Payload"]!.Deserialize<InstitutionBootstrapCompleted>()!;
        EqualJson(fixture["Payload"]!, result);
        EqualJson(fixture["Reference"]!, ProvisioningBootstrapPost.ResultReference());
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(new[] { "University", "Campus", "AdministrationFaculty", "Decisions" },
            result.Steps.Select(step => step.Step));
        if (succeeded) Assert.All(result.Steps, step => Assert.Equal(BootstrapStepStatus.Succeeded, step.Status));
        else
        {
            Assert.Equal(BootstrapStepStatus.Failed, result.Steps[1].Status);
            Assert.Equal("operation.failed", Assert.Single(result.Steps[1].Issues));
            Assert.All(result.Steps.Skip(2), step => Assert.Equal(BootstrapStepStatus.NotAttempted, step.Status));
        }
    }

    [Fact]
    public void Request_identity_and_result_causation_match_fixtures()
    {
        var fixture = Fixture("request");
        var payload = fixture["Payload"]!.Deserialize<InstitutionBootstrapRequested>()!;
        var expected = ReadMetadata(fixture["Metadata"]!);
        Guid Id(string key) => Guid.Parse(expected.Attributes[key]);
        var metadata = BootstrapPostMetadata.CreateRequest(payload.RequestId,
            Id(BootstrapPostMetadata.UniversityRequestId), Id(BootstrapPostMetadata.CampusRequestId),
            Id(BootstrapPostMetadata.FacultyRequestId), Id(BootstrapPostMetadata.DecisionsRequestId), payload.RequestedAtUtc);
        EqualJson(fixture["Metadata"]!, metadata);
        var completed = ReadMetadata(Fixture("completed")["Metadata"]!);
        EqualJson(Fixture("completed")["Metadata"]!, BootstrapPostMetadata.CreateCompletion(metadata,
            completed.MessageId, completed.ProducedAtUtc));
        Assert.Null(metadata.CausationId); // Operator-originated envelope, not a preceding Post message.
        Assert.NotEqual(metadata.MessageId, metadata.CorrelationId);
    }

    [Fact]
    public void Completion_does_not_reflect_unrelated_headers_and_supports_legacy_v1_metadata()
    {
        var legacy = new PostMetadata(messageId: "legacy-message", attributes: new Dictionary<string, string>
            { ["unrelated"] = "must-not-be-copied" });
        var result = BootstrapPostMetadata.CreateCompletion(legacy);
        Assert.Equal("legacy-message", result.CorrelationId);
        Assert.Equal("legacy-message", result.CausationId);
        Assert.Empty(result.Attributes);
    }

    [Fact]
    public void Empty_or_reused_request_ids_are_rejected()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Throws<ArgumentException>(() => BootstrapPostMetadata.CreateRequest(Guid.Empty,
            ids[1], ids[2], ids[3], ids[4], DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => BootstrapPostMetadata.CreateRequest(ids[0],
            ids[1], ids[2], ids[3], ids[1], DateTimeOffset.UtcNow));
    }
}
