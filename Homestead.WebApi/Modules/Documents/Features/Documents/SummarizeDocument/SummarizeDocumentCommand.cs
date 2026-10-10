namespace Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;

public record SummarizeDocumentCommand
{
    public Guid DocumentId { get; init; }
}
