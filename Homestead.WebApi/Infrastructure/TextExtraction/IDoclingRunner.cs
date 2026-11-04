namespace Homestead.WebApi.Infrastructure.TextExtraction;

// What one conversion asks of docling-serve. FileName is the storage file name (hash-based), never the
// document's title — the sidecar logs it. DocumentTimeout is sent as document_timeout
// (whole seconds, at least 1) when set, less any time spent waiting for a conversion slot; the engine
// always sets it.
public sealed record DoclingConvertRequest(
    string FileName, string ContentType, string? TableMode, bool ForceOcr, TimeSpan? DocumentTimeout);

// The conversion result as docling-serve reported it. Status is its ConversionStatus ("success",
// "partial_success", "skipped", "failure"), or "unknown" when the response carried none; Errors are its
// error_message strings. ServerVersion is
// best-effort (null when the sidecar hides /version).
public sealed record DoclingConvertResult(
    string? Markdown, string Status, IReadOnlyList<string> Errors, double? ProcessingSeconds, string? ServerVersion);

public interface IDoclingRunner
{
    Task<DoclingConvertResult> ConvertAsync(Stream document, DoclingConvertRequest request, CancellationToken ct);
}
