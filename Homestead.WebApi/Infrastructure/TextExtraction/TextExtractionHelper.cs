using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TextExtractionHelper(
    IServiceProvider serviceProvider,
    ITextExtractionEngineResolver engineResolver)
{
    public async Task<string?> ExtractTextSafeAsync(
        string filePath, string contentType, Guid? engineId,
        ILogger logger, CancellationToken ct)
    {
        try
        {
            var context = await engineResolver.ResolveAsync(engineId, ct);
            var engine = serviceProvider.GetKeyedService<ITextExtractionEngine>(context.EngineType);
            if (engine is null)
                return null;

            return await engine.ExtractTextAsync(filePath, contentType, context.Configuration, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Text extraction failed for {FilePath}", filePath);
            return null;
        }
    }
}
