namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Everything a single tesseract invocation needs. Passed per call rather than captured from IOptions at
// construction: the runner is one shared singleton behind potentially several differently-configured
// Tesseract engine instances, so it cannot hold any one instance's tessdata path, psm or oem.
public record TesseractRunOptions(string Language, string TessDataPath, int? Dpi, int Psm, int Oem);

public interface ITesseractRunner
{
    Task<string?> OcrAsync(Stream imageStream, TesseractRunOptions options, CancellationToken ct);
}
