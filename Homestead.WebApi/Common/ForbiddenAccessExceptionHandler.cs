namespace Homestead.WebApi.Common;

public class ForbiddenAccessExceptionHandler()
    : DomainExceptionHandler<ForbiddenAccessException>(StatusCodes.Status403Forbidden);
