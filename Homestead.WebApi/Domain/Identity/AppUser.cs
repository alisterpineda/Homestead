namespace Homestead.WebApi.Domain.Identity;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AppRole Role { get; set; } = AppRole.Contributor;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
