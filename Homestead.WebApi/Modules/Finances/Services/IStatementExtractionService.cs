using Homestead.WebApi.Modules.Finances.Processing.Statements;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Extracts a faithful <see cref="StatementIr"/> (header only in Phase 1) from a statement's OCR/extracted
/// text, optionally supplemented by a rendered page image for scanned statements. A hard AI dependency —
/// callers gate on the resolved <c>IAiProvider</c> being available before invoking.
/// </summary>
public interface IStatementExtractionService
{
    Task<StatementIr> ExtractAsync(string extractedText, byte[]? pageImage, CancellationToken ct);
}
