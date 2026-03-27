using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Features.Documents.Processing;

public class TextExtractionFailedException(
    string message,
    Exception innerException) : Exception(message, innerException), IStructuredJobError
{
    public string ErrorCode => "text_extraction_failed";

    public Dictionary<string, string> ErrorData { get; } = new()
    {
        ["innerError"] = innerException.Message
    };
}
