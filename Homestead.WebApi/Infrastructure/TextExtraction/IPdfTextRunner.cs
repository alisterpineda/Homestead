namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Per-page text of a PDF's embedded text layer, one line per printed row with columns kept apart (padding
// compacted to two-space gaps — see PdfTextRunner.CompactLayout). Null = the tool is unavailable or
// failed (the caller falls back to PdfPig); an empty string in the list = that page has no text layer.
public interface IPdfTextRunner
{
    Task<IReadOnlyList<string>?> ExtractPagesAsync(string filePath, CancellationToken ct);
}
