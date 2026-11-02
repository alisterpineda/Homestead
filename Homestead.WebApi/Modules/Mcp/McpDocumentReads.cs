using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Mcp;

// The single-document reads the tools need, projected to exactly what they use. GetDocumentHandler serves the web
// app's document page (the latest version's whole extracted text, its provenance, the version list); a tool calling it
// to read a few fields would de-TOAST and allocate the full text on every call. Read-only (AsNoTracking) and with no
// access check of its own: callers run it inside McpToolInvoker.ReadDocumentAsync / WriteDocumentAsync, after the
// frame has authorized the id. Active documents only (the soft-delete query filter), so null = trashed or missing.
public sealed class McpDocumentReads(AppDbContext db)
{
    // What the write tools read before and after a change: the editable fields and the pending-suggestion count.
    public async Task<McpDocumentEdit?> EditStateAsync(Guid documentId, CancellationToken ct)
    {
        var d = await db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.DocumentDate,
                d.ExpiryDate,
                d.Notes,
                TagPaths = d.DocumentTags.Select(dt => dt.Tag.Path).ToList(),
                Parties = d.DocumentParties.Select(dp => new { dp.Party.Id, dp.Party.Name }).ToList(),
                PendingSuggestionCount = d.Suggestions.Count(s => s.Status == DocumentSuggestionStatus.Pending),
            })
            .FirstOrDefaultAsync(ct);

        return d is null
            ? null
            : new McpDocumentEdit(
                d.Id,
                new DocumentEditState(
                    d.Name, d.DocumentDate, d.ExpiryDate, d.Notes, d.TagPaths,
                    d.Parties.Select(p => new ResolvedParty(p.Id, p.Name)).ToList()),
                d.PendingSuggestionCount);
    }

    // get_document's metadata: the latest version's text is measured (length(), in code points), never selected.
    // Null also when the document has no version (as GetDocumentHandler). With include_text, the total that
    // get_document reports comes from TextChunkAsync instead, measured with the chunk.
    public async Task<McpDocumentDetails?> DetailsAsync(Guid documentId, CancellationToken ct)
    {
        var d = await db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.DocumentDate,
                d.ExpiryDate,
                d.Notes,
                d.Summary,
                d.CreatedAt,
                d.UpdatedAt,
                LatestVersion = d.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new { v.Id, v.FileExtension, v.FileSize, TextLength = (int?)v.ExtractedText!.Length })
                    .FirstOrDefault(),
                VersionCount = d.Versions.Count,
                Tags = d.DocumentTags.Select(dt => new DocumentTagResult(dt.Tag.Path, dt.Tag.Description)).ToList(),
                Parties = d.DocumentParties
                    .Select(dp => new DocumentPartyResult(dp.Party.Name, dp.Party.Description)).ToList(),
                Suggestions = d.Suggestions
                    .Where(s => s.Status == DocumentSuggestionStatus.Pending)
                    .Select(s => new McpPendingSuggestion(s.Id, s.Field, s.SuggestedValue, s.OriginalValue, s.CreatedAt))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (d?.LatestVersion is null)
            return null;

        return new McpDocumentDetails(
            d.Id, d.Name, d.DocumentDate, d.ExpiryDate, d.Notes, d.Summary,
            d.LatestVersion.FileExtension, d.LatestVersion.FileSize, d.VersionCount, d.CreatedAt, d.UpdatedAt,
            d.Tags, d.Parties, d.Suggestions,
            d.LatestVersion.Id, d.LatestVersion.TextLength ?? 0);
    }

    // Up to maxChars of a version's extracted text from offset, cut in SQL (substring, code points) — only the chunk
    // crosses the wire — and measured (length()) in the same statement, so the total and the chunk describe one text:
    // a reprocess committing after DetailsAsync cannot pair a new chunk with an old total. NULL text = no text.
    public async Task<McpTextChunk> TextChunkAsync(Guid versionId, int offset, int maxChars, CancellationToken ct)
    {
        var start = McpText.SqlStart(offset);
        var row = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.Id == versionId)
            .Select(v => new
            {
                TotalChars = (int?)v.ExtractedText!.Length,
                Text = v.ExtractedText.Substring(start, maxChars),
            })
            .FirstOrDefaultAsync(ct);

        return new McpTextChunk(row?.TotalChars ?? 0, row?.Text ?? "");
    }
}

public sealed record McpTextChunk(int TotalChars, string Text);

public sealed record McpDocumentEdit(Guid Id, DocumentEditState State, int PendingSuggestionCount);

public sealed record McpDocumentDetails(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string Notes,
    string? Summary,
    string FileExtension,
    long FileSize,
    int VersionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DocumentTagResult> Tags,
    IReadOnlyList<DocumentPartyResult> Parties,
    IReadOnlyList<McpPendingSuggestion> PendingSuggestions,
    Guid LatestVersionId,
    int TextTotalChars);

// A pending suggestion as stored: for Tags/Parties, SuggestedValue is the tag/party id.
public sealed record McpPendingSuggestion(
    Guid Id, DocumentSuggestionField Field, string SuggestedValue, string? OriginalValue, DateTimeOffset CreatedAt);
