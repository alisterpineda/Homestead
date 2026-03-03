namespace Homestead.WebApi.Common;

public class BadRequestException : Exception
{
    public object? Errors { get; }

    public BadRequestException(string message) : base(message) { }

    public BadRequestException(object errors, string message = "One or more validation errors occurred.")
        : base(message)
    {
        Errors = errors;
    }
}
