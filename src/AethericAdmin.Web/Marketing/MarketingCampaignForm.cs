using MarketingCampus.Core.Campaigns;
namespace AethericAdmin.Web.Marketing;

public sealed class MarketingCampaignForm
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WebsiteId { get; set; } = "aetheric-forge";
    public string IntakePath { get; set; } = "/projects";
    public string Audience { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    public string ActionUrl { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ImageAlt { get; set; } = "";
    public string OfferingIds { get; set; } = "";
    public string SeoTitle { get; set; } = "";
    public string SeoDescription { get; set; } = "";
    public string SocialTitle { get; set; } = "";
    public string SocialDescription { get; set; } = "";
    public string SocialImageUrl { get; set; } = "";
    public string SocialImageAlt { get; set; } = "";
    public string StructuredData { get; set; } = "";
    public long Revision { get; set; }
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public CampaignDraft ToDraft() => new(Id, Name, WebsiteId, IntakePath, Optional(Audience), Optional(Objective),
        new(Optional(Headline), Optional(Summary), Optional(Body),
            Optional(ActionLabel) is null && Optional(ActionUrl) is null ? null : new(ActionLabel, ActionUrl),
            Optional(ImageUrl) is null && Optional(ImageAlt) is null ? null : new(ImageUrl, ImageAlt),
            OfferingIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            new(Optional(SeoTitle), Optional(SeoDescription), Optional(SocialTitle), Optional(SocialDescription),
                Optional(SocialImageUrl) is null && Optional(SocialImageAlt) is null ? null : new(SocialImageUrl, SocialImageAlt),
                Optional(StructuredData))), Optional(Notes), Revision);
    public static MarketingCampaignForm FromDraft(CampaignDraft draft) => new()
    {
        Id=draft.Id, Name=draft.Name, WebsiteId=draft.WebsiteId, IntakePath=draft.IntakePath,
        Audience=draft.Audience??"", Objective=draft.Objective??"", Notes=draft.Notes??"", Revision=draft.Revision,
        Headline=draft.Content?.Headline??"", Summary=draft.Content?.Summary??"", Body=draft.Content?.Body??"",
        ActionLabel=draft.Content?.CallToAction?.Label??"", ActionUrl=draft.Content?.CallToAction?.Href??"",
        ImageUrl=draft.Content?.HeroImage?.Url??"", ImageAlt=draft.Content?.HeroImage?.AlternativeText??"",
        OfferingIds=string.Join(", ", draft.Content?.OfferingIds??[]), SeoTitle=draft.Content?.Seo?.Title??"",
        SeoDescription=draft.Content?.Seo?.Description??"", SocialTitle=draft.Content?.Seo?.SocialTitle??"",
        SocialDescription=draft.Content?.Seo?.SocialDescription??"", SocialImageUrl=draft.Content?.Seo?.SocialImage?.Url??"",
        SocialImageAlt=draft.Content?.Seo?.SocialImage?.AlternativeText??"", StructuredData=draft.Content?.Seo?.StructuredDataJson??""
    };
}
