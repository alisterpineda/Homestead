namespace HomeSuiteHome.WebApi.Authentication;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public ClaimMappingOptions ClaimMappings { get; set; } = new();
}
