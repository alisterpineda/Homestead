namespace Homestead.WebApi.Modules.Documents.Processing;

public record UploadDocumentVersionPayload(
    Guid UploadId,
    Guid DocumentId,
    string OriginalFileName,
    string? Label,
    string ContentType,
    long FileSize,
    string FileExtension,
    Guid? TextExtractionEngineId = null);
