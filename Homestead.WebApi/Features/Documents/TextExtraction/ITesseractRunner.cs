namespace Homestead.WebApi.Features.Documents.TextExtraction;

public interface ITesseractRunner
{
    Task<string?> OcrAsync(Stream imageStream, string language, CancellationToken ct);
}
