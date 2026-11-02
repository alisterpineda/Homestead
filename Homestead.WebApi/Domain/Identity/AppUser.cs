namespace Homestead.WebApi.Domain.Identity;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The OIDC subject (<c>sub</c>) of the identity this account is linked to. Recorded by the web sign-in on
    /// every callback; the MCP bearer door resolves users by it (lookup only). Null on rows that have not signed
    /// in through the web since it was introduced.
    /// </summary>
    public string? ExternalSubject { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public AppRole Role { get; set; } = AppRole.Contributor;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
