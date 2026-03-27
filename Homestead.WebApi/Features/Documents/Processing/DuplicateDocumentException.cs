using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Features.Documents.Processing;

public class DuplicateDocumentException(
    string message,
    Guid existingDocumentId,
    string existingDocumentName,
    bool isTrashed = false) : Exception(message), IStructuredJobError
{
    public Guid ExistingDocumentId { get; } = existingDocumentId;
    public string ExistingDocumentName { get; } = existingDocumentName;
    public bool IsTrashed { get; } = isTrashed;

    public string ErrorCode => "duplicate_content";

    public Dictionary<string, string> ErrorData { get; } = new()
    {
        ["documentId"] = existingDocumentId.ToString(),
        ["documentName"] = existingDocumentName,
        ["isTrashed"] = isTrashed.ToString().ToLowerInvariant()
    };
}
