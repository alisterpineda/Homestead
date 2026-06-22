namespace Homestead.WebApi.Infrastructure.TextExtraction;

public interface ITesseractRunner
{
    Task<string?> OcrAsync(Stream imageStream, string language, int? dpi, CancellationToken ct);
}
