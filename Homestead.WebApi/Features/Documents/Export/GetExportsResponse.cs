using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Features.Documents.Export;

public record GetExportsResponse
{
    public required Guid Id { get; init; }
    public required ProcessingJobStatus Status { get; init; }
    public string? Error { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int? DocumentCount { get; init; }
    public long? ZipFileSize { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
