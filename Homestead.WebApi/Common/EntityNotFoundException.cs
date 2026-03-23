namespace Homestead.WebApi.Common;

public class EntityNotFoundException(string message = "The requested resource was not found.") : Exception(message);
