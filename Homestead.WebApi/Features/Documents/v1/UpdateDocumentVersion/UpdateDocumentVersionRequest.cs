namespace Homestead.WebApi.Features.Documents.v1.UpdateDocumentVersion;

public record UpdateDocumentVersionRequest
{
    public string? Label { get; init; }
    public string? Notes { get; init; }
}

public record UpdateDocumentVersionCommand
{
    public Guid DocumentId { get; init; }
    public Guid Id { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
}
