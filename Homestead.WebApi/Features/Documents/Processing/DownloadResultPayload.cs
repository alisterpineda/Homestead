namespace Homestead.WebApi.Features.Documents.Processing;

public record DownloadResultPayload(
    long ZipFileSize,
    int DocumentCount,
    DateTimeOffset ExpiresAt);
