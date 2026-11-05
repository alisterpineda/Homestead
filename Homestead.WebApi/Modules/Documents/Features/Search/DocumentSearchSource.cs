using System.Globalization;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Search;

/// <summary>
/// Universal-search source for documents. Mirrors the document list page exactly — same access
/// scoping (<see cref="IDocumentQueryService.AccessibleDocuments"/>), the same <see cref="DocumentSearchTerms"/>
/// (its filter, tsquery and rank) in the list page's <see cref="DocumentMatchMode.Substring"/> mode, and the same
/// FTS <c>ts_headline</c> snippet — so palette results equal list results for the same query. Soft-deleted
/// documents are excluded by the default <see cref="TrashFilter.ActiveOnly"/> scope.
/// </summary>
public class DocumentSearchSource(AppDbContext db, IDocumentQueryService accessService) : ISearchSource
{
    private const string TsLanguage = DocumentSearchTerms.TsLanguage;

    public string Source => "documents";
    public string Label => "Documents";

    public async Task<SearchSourceResult> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var terms = await DocumentSearchTerms.BuildAsync(db, query, DocumentMatchMode.Substring, ct);
        var tsQueryString = terms.TsQuery;

        var filtered = accessService.AccessibleDocuments().AsNoTracking().Where(terms.Any);

        // Full (uncapped) match count drives "View all (N)".
        var total = await filtered.CountAsync(ct);
        if (total == 0)
            return new SearchSourceResult(0, []);

        // Relevance first when we have an FTS query (the rank is 0 for non-FTS matches — ILIKE on name/tag/party —
        // so the UpdatedAt tiebreaker gives those rows a deterministic order). When the query reduced to nothing
        // (all stopwords/punctuation), there's no tsquery, so order purely by recency.
        var ordered = tsQueryString is not null
            ? filtered.OrderByDescending(terms.Rank).ThenByDescending(d => d.UpdatedAt).ThenBy(d => d.Id)
            : filtered.OrderByDescending(d => d.UpdatedAt).ThenBy(d => d.Id);

        // Take BEFORE projecting so the correlated subqueries + ts_headline run for <= limit rows only —
        // the same per-page cost the list already pays, well within the 3s per-source soft timeout.
        var rows = await ordered
            .Take(limit)
            .Select(d => new DocumentRow(
                d.Id,
                d.Name,
                d.DocumentDate,
                d.CreatedAt,
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ContentType).FirstOrDefault(),
                // Mirror of GetDocumentsHandler's MatchSnippet: only generate a headline when the FTS vector actually
                // matched (name/tag/party-only matches produce no content snippet). The query is already folded and
                // the text is folded here, as the stored vector is, so the headline finds the match.
                (tsQueryString != null && d.SearchVector != null
                    && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, tsQueryString)))
                    ? EF.Functions.ToTsQuery(TsLanguage, tsQueryString)
                        .GetResultHeadline(
                            TsLanguage,
                            EF.Functions.Unaccent(d.Versions.OrderByDescending(v => v.VersionNumber)
                                .Select(v => v.ExtractedText).FirstOrDefault() ?? string.Empty),
                            MatchSnippetSanitizer.HeadlineOptions)
                    : null))
            .ToListAsync(ct);

        var hits = rows.Select(ToHit).ToList();
        return new SearchSourceResult(total, hits);
    }

    private SearchHit ToHit(DocumentRow r)
    {
        var type = r.LatestContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            ? "image"
            : "document";

        // Subtitle: a plain-text match snippet (why it matched) when the content matched; otherwise the
        // document date. No badge per product decision.
        var subtitle = PlainSnippet(r.Headline) ?? FormatDate(r.DocumentDate, r.CreatedAt);

        return new SearchHit(Source, type, r.Id.ToString(), r.Name, subtitle, Badge: null);
    }

    // Strip the headline's STX/ETX match sentinels to plain text — the palette renders the subtitle via
    // text interpolation, so no HTML. Returns null when there's no content match (same rule the list's
    // Sanitize uses: no StartSentinel → name/tag/party-only match → no snippet).
    private static string? PlainSnippet(string? headline)
    {
        if (string.IsNullOrEmpty(headline) || !headline.Contains(MatchSnippetSanitizer.StartSentinel))
            return null;

        return headline
            .Replace(MatchSnippetSanitizer.StartSentinel, string.Empty)
            .Replace(MatchSnippetSanitizer.StopSentinel, string.Empty)
            .Trim();
    }

    // Fallback subtitle: the document's own date, else its upload date — e.g. "12 Mar 2024".
    private static string FormatDate(DateOnly? documentDate, DateTimeOffset createdAt) =>
        (documentDate ?? DateOnly.FromDateTime(createdAt.UtcDateTime))
            .ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private sealed record DocumentRow(
        Guid Id,
        string Name,
        DateOnly? DocumentDate,
        DateTimeOffset CreatedAt,
        string? LatestContentType,
        string? Headline);
}
