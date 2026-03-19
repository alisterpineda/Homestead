namespace Homestead.WebApi.TextExtraction;

public interface ITextExtractionEngineResolver
{
    Task<TextExtractionContext> ResolveAsync(Guid? engineId, CancellationToken ct);
}
