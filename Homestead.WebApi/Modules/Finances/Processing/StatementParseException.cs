using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Modules.Finances.Processing;

/// <summary>
/// Structured failure for a statement-parse job. Surfaces a stable <see cref="ErrorCode"/> to the frontend
/// (e.g. <c>ai_unavailable</c> when the AI integration is unconfigured) via the processor's failure notify.
/// </summary>
public class StatementParseException : Exception, IStructuredJobError
{
    public StatementParseException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
        ErrorData = new Dictionary<string, string>();
    }

    public string ErrorCode { get; }
    public Dictionary<string, string> ErrorData { get; }
}
