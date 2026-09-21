using Aetheric.Provisioning.Definitions;
using Aetheric.Provisioning.Worker;
using AethericAdmin.Web.Provisioning;
using Xunit;

namespace AethericAdmin.Tests;

public sealed class BootstrapDefinitionBuilderTests
{
    private static UniversityEnvelopeDraft Draft()
    {
        var draft = new UniversityDraft
        {
            UniversityName = "Example University",
            CampusName = "Main Campus",
            FacultyName = "Administration",
            DecisionsName = "Decisions",
            KeycloakAuthority = "https://sso.example.org",
            KeycloakRealm = "example-university",
            KeycloakClientId = "admin-cli",
            RootAdministratorSubjectId = "operator-subject",
        };
        return draft.Review();
    }

    // BootstrapDefinitionBuilder returns admin's own InstitutionConfig (InstitutionBootstrapMessages.cs) -
    // independently defined from runtime Worker's identically-shaped type, matching the org's
    // established "no shared contracts package" convention (JSON-structural, not type-identical).
    // Reading generated YAML through the real InstitutionYamlReader needs the Worker's own type.
    private static Aetheric.Provisioning.Worker.InstitutionConfig ForReader(AethericAdmin.Web.Provisioning.InstitutionConfig config) =>
        new(config.DefinitionYaml, config.BindingsYaml);

    [Fact]
    public void Generated_definitions_pass_the_runtime_reader_for_all_four_institutions()
    {
        var (university, campus, faculty, decisions) = BootstrapDefinitionBuilder.Build(Draft());
        foreach (var (id, config) in new[] { ("university", university), ("campus", campus),
            ("administration-faculty", faculty), ("decisions", decisions) })
        {
            var bundle = InlineSourceDocuments.Build(id, ForReader(config));
            var result = new InstitutionYamlReader().Read(bundle);
            Assert.True(result.Institution is not null,
                $"{id}: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Target} - {i.Message}"))}");
        }
    }

    [Fact]
    public void University_binds_registry_to_the_drafts_keycloak_realm()
    {
        var (university, _, _, _) = BootstrapDefinitionBuilder.Build(Draft());
        var bundle = InlineSourceDocuments.Build("university", ForReader(university));
        var loaded = new InstitutionYamlReader().Read(bundle).Institution!;
        Assert.Equal("keycloak", loaded.Bindings.Resources["registry"].Provider);
        Assert.Equal("example-university", loaded.Bindings.Resources["registry"].Settings["realm"]);
    }

    [Fact]
    public void Campus_and_decisions_use_matching_workbench_targets()
    {
        var (_, campus, _, decisions) = BootstrapDefinitionBuilder.Build(Draft());
        var campusLoaded = new InstitutionYamlReader().Read(InlineSourceDocuments.Build("campus", ForReader(campus))).Institution!;
        var decisionsLoaded = new InstitutionYamlReader().Read(InlineSourceDocuments.Build("decisions", ForReader(decisions))).Institution!;
        Assert.Equal(BootstrapDefinitionBuilder.WorkbenchTarget, campusLoaded.Bindings.Resources["workbench"].Settings["target"]);
        Assert.Equal(BootstrapDefinitionBuilder.WorkbenchTarget, decisionsLoaded.Bindings.Resources["draft-workspace"].Settings["target"]);
        Assert.NotEqual(campusLoaded.Bindings.Resources["workbench"].Settings["stage"],
            decisionsLoaded.Bindings.Resources["draft-workspace"].Settings["stage"]);
    }

    [Fact]
    public void Decisions_sources_its_inherited_capabilities_from_campus_and_university()
    {
        var (_, _, _, decisions) = BootstrapDefinitionBuilder.Build(Draft());
        var loaded = new InstitutionYamlReader().Read(InlineSourceDocuments.Build("decisions", ForReader(decisions))).Institution!;
        Assert.Equal("campus.archive", loaded.Bindings.ParentSources["IArchive"]);
        Assert.Equal("campus.library", loaded.Bindings.ParentSources["ILibrary"]);
        Assert.Equal("campus.post-office", loaded.Bindings.ParentSources["IPostOffice"]);
        Assert.Equal("university.registry", loaded.Bindings.ParentSources["IRegistrar"]);
    }
}
