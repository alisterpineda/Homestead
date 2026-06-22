namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TextExtractionHelper(ITextExtractionEngine engine)
{
    public async Task<TextExtractionResult> ExtractTextSafeAsync(
        string filePath, string contentType, CancellationToken ct)
    {
        try
        {
            var text = await engine.ExtractTextAsync(filePath, contentType, ct);
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
