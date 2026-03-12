namespace Homestead.WebApi.Features.Identity.v1.Login;

public record LoginResponse(bool Succeeded, bool IsLockedOut);
