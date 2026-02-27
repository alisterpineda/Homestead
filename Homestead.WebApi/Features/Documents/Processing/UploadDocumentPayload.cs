namespace Homestead.WebApi.Features.Documents.Processing;

public record UploadDocumentPayload(
    Guid UploadId,
    string OriginalFileName,
    string? Name,
    string? Label,
    string ContentType,
    long FileSize,
    string FileExtension);
