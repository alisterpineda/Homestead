namespace Homestead.WebApi.Modules.Documents.Processing;

public record UploadDocumentPayload(
    Guid UploadId,
    string OriginalFileName,
    string? Name,
    string? Label,
    string ContentType,
    long FileSize,
    string FileExtension,
    Guid? GroupId = null,
    Guid? TextExtractionEngineId = null);
