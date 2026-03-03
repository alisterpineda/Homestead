namespace Homestead.WebApi.Authorization;

public record DocumentAccessResult(bool IsAllowed, bool CanWrite);
