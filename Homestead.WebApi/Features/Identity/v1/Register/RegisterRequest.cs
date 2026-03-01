namespace Homestead.WebApi.Features.Identity.v1.Register;

public record RegisterRequest(string Email, string DisplayName, string Password);
