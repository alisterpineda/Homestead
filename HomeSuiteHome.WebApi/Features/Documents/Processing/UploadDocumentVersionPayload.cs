namespace HomeSuiteHome.WebApi.Features.Documents.Processing;

public record UploadDocumentVersionPayload(
    Guid UploadId,
    Guid DocumentId,
    string OriginalFileName,
    string? Label,
    string? Notes,
    string ContentType,
    long FileSize,
    string FileExtension);
