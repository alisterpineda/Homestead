namespace Homestead.WebApi.Modules.Identity.Features.Login;

public record LoginResponse(bool Succeeded, bool IsLockedOut, bool IsInactive);
