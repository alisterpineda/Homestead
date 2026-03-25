namespace Homestead.WebApi.Infrastructure.TextExtraction;

public interface ITextExtractionEngineResolver
{
    Task<TextExtractionContext> ResolveAsync(Guid? engineId, CancellationToken ct);
}
