using Microsoft.AspNetCore.Identity;

namespace HomeSuiteHome.WebApi.Domains.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
