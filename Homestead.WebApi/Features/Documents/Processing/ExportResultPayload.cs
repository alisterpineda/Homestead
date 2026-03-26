namespace Homestead.WebApi.Features.Documents.Processing;

public record ExportResultPayload(
    long ZipFileSize,
    int DocumentCount,
    DateTimeOffset ExpiresAt);
