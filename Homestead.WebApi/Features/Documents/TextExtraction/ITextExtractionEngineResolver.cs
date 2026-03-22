namespace Homestead.WebApi.Features.Documents.TextExtraction;

public interface ITextExtractionEngineResolver
{
    Task<TextExtractionContext> ResolveAsync(Guid? engineId, CancellationToken ct);
}
