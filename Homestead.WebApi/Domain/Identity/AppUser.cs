using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Domain.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
