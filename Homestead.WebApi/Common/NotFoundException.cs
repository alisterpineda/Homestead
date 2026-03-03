namespace Homestead.WebApi.Common;

public class NotFoundException(string message = "The requested resource was not found.") : Exception(message);
