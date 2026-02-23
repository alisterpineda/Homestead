namespace HomeSuiteHome.WebApi.Authentication;

public class ClaimMappingOptions
{
    public string Subject { get; set; } = "sub";
    public string Email { get; set; } = "email";
    public string Name { get; set; } = "preferred_username";
    public string Roles { get; set; } = "roles";
}
