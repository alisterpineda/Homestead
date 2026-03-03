namespace Homestead.WebApi.Common;

public class ConflictExceptionHandler()
    : DomainExceptionHandler<ConflictException>(StatusCodes.Status409Conflict);
