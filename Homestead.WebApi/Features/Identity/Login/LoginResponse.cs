namespace Homestead.WebApi.Features.Identity.Login;

public record LoginResponse(bool Succeeded, bool IsLockedOut, bool IsInactive);
