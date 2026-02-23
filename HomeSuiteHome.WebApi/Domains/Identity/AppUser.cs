namespace HomeSuiteHome.WebApi.Domains.Identity;

public class AppUser
{
    public Guid Id { get; set; }
    public required string ExternalSubject { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
