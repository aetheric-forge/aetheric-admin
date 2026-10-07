using AethericAdmin.Web.Marketing;
using MarketingCampus.Core.Campaigns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace AethericAdmin.Tests;
public sealed class MarketingIntegrationTests
{
    [Fact]
    public void PlatformMongoDoesNotEnableMarketingOrInheritItsCredentials()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["MongoDb:Host"]="localhost",["MongoDb:Username"]="platform",["MongoDb:Password"]="platform-secret"
        }).Build();
        var services = new ServiceCollection().AddMarketingManagement(config).BuildServiceProvider();
        Assert.False(services.GetRequiredService<MarketingAvailability>().Enabled);
    }
    [Fact]
    public void ExplicitMarketingConfigurationRequiresInstitutionCredentials()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Marketing:MongoDb:Host"]="localhost",["MongoDb:Username"]="platform",["MongoDb:Password"]="platform-secret"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMarketingManagement(config));
    }
    [Fact]
    public void HostFormPreservesDraftRevisionContentAndPrivateNotes()
    {
        var draft = new CampaignDraft("launch","Launch","aetheric-forge","/projects","Audience","Objective",
            new("Headline","Summary",CallToAction:new("Contact","/contact"),OfferingIds:["consulting"],Seo:new("Title","Description")),"Private notes",4);
        var roundtrip = MarketingCampaignForm.FromDraft(draft).ToDraft();
        Assert.Equal(draft.Id,roundtrip.Id);
        Assert.Equal(draft.Revision,roundtrip.Revision);
        Assert.Equal(draft.Notes,roundtrip.Notes);
        Assert.Equal(draft.Content!.Headline,roundtrip.Content!.Headline);
        Assert.Equal(draft.Content.OfferingIds,roundtrip.Content.OfferingIds);
        Assert.Equal(draft.Content.Seo!.Title,roundtrip.Content.Seo!.Title);
    }
}
