namespace HomeSuiteHome.WebApi.Features.Documents.Processing;

public record UploadDocumentPayload(
    Guid UploadId,
    string OriginalFileName,
    string? Name,
    string ContentType,
    long FileSize,
    string FileExtension);
