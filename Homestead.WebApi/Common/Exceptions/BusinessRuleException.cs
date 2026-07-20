namespace Homestead.WebApi.Common;

public class BusinessRuleException : Exception
{
    public object? Errors { get; }

    /// <summary>
    /// Optional stable machine code surfaced as the problem-details <c>errorCode</c> extension, so the
    /// frontend can react to a specific rule violation without parsing the human message. Null for the
    /// common case (a plain 400 the client only displays).
    /// </summary>
    public string? ErrorCode { get; }

    public BusinessRuleException(string message, string? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }

    public BusinessRuleException(object errors, string message = "One or more validation errors occurred.")
        : base(message)
    {
        Errors = errors;
    }
}
