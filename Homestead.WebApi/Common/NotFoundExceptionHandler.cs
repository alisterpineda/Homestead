namespace Homestead.WebApi.Common;

public class NotFoundExceptionHandler()
    : DomainExceptionHandler<NotFoundException>(StatusCodes.Status404NotFound);
