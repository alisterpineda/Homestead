namespace Homestead.WebApi.Common;

public class EntityInUseException(string message) : Exception(message);
