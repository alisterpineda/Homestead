namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocumentRevision;

public record UpdateDocumentRevisionRequest
{
    public string? Label { get; init; }
    public string? Notes { get; init; }
}

public record UpdateDocumentRevisionCommand
{
    public Guid DocumentId { get; init; }
    public Guid Id { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
}
