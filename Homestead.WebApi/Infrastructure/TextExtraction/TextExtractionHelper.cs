using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TextExtractionHelper(
    IServiceProvider serviceProvider,
    ITextExtractionEngineResolver engineResolver)
{
    public async Task<TextExtractionResult> ExtractTextSafeAsync(
        string filePath, string contentType, Guid? engineId,
        CancellationToken ct)
    {
        try
        {
            var context = await engineResolver.ResolveAsync(engineId, ct);
            var engine = serviceProvider.GetKeyedService<ITextExtractionEngine>(context.EngineType);
            if (engine is null)
                return TextExtractionResult.NoEngineAvailable;

            var text = await engine.ExtractTextAsync(filePath, contentType, context.Configuration, ct);
            return TextExtractionResult.Success(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TextExtractionResult.Failure(ex);
        }
    }
}
