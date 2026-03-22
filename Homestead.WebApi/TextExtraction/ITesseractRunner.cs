namespace Homestead.WebApi.TextExtraction;

public interface ITesseractRunner
{
    Task<string?> OcrAsync(Stream imageStream, string language, CancellationToken ct);
}
