namespace Homestead.WebApi.Authentication;

public class OidcOptions
{
    public const string SectionName = "Oidc";
    public const string SchemeName = "oidc";

    public string Authority { get; set; } = "";
    public string? MetadataAddress { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string DisplayName { get; set; } = "SSO";
    public List<string> AdditionalScopes { get; set; } = [];
    public string? RoleClaimType { get; set; }
    public List<string> AdminClaimValues { get; set; } = [];

    public bool IsConfigured =>
        Authority.Length > 0 && ClientId.Length > 0 && ClientSecret.Length > 0;
}
