using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Features.Documents.Processing;

public class DuplicateDocumentException(
    string message,
    Guid existingDocumentId,
    string existingDocumentName) : Exception(message), IStructuredJobError
{
    public Guid ExistingDocumentId { get; } = existingDocumentId;
    public string ExistingDocumentName { get; } = existingDocumentName;

    public string ErrorCode => "duplicate_content";

    public Dictionary<string, string> ErrorData { get; } = new()
    {
        ["documentId"] = existingDocumentId.ToString(),
        ["documentName"] = existingDocumentName
    };
}
