namespace Homestead.WebApi.Features.Documents.v1.UpdateDocument;

public record UpdateDocumentRequest
{
    public required string Name { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public List<string> TagPaths { get; init; } = [];
    public List<Guid> PartyIds { get; init; } = [];
}

public record UpdateDocumentCommand
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public List<string> TagPaths { get; init; } = [];
    public List<Guid> PartyIds { get; init; } = [];
}
