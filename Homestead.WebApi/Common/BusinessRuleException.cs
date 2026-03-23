namespace Homestead.WebApi.Common;

public class BusinessRuleException : Exception
{
    public object? Errors { get; }

    public BusinessRuleException(string message) : base(message) { }

    public BusinessRuleException(object errors, string message = "One or more validation errors occurred.")
        : base(message)
    {
        Errors = errors;
    }
}
