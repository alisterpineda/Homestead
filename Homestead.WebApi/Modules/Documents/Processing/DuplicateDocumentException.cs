using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class DuplicateDocumentException(
    string message,
    Guid existingDocumentId,
    string existingDocumentName,
    int existingVersionNumber,
    bool isSameDocument,
    bool isTrashed = false) : Exception(message), IStructuredJobError
{
    public Guid ExistingDocumentId { get; } = existingDocumentId;
    public string ExistingDocumentName { get; } = existingDocumentName;
    public int ExistingVersionNumber { get; } = existingVersionNumber;
    public bool IsSameDocument { get; } = isSameDocument;
    public bool IsTrashed { get; } = isTrashed;

    public string ErrorCode => "duplicate_content";

    public Dictionary<string, string> ErrorData { get; } = new()
    {
        ["documentId"] = existingDocumentId.ToString(),
        ["documentName"] = existingDocumentName,
        ["versionNumber"] = existingVersionNumber.ToString(),
        ["isSameDocument"] = isSameDocument.ToString().ToLowerInvariant(),
        ["isTrashed"] = isTrashed.ToString().ToLowerInvariant()
    };
}
