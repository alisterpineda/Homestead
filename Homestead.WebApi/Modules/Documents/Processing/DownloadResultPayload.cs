namespace Homestead.WebApi.Modules.Documents.Processing;

public record DownloadResultPayload(
    long ZipFileSize,
    int DocumentCount,
    DateTimeOffset ExpiresAt);
