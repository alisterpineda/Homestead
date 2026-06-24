using Homestead.WebApi.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

/// <summary>
/// The document list's five-mode text-search predicate, shared so the document list page and the
/// universal-search palette can never silently diverge on what "matches" a query. Both surfaces call
/// <see cref="ApplyTextSearch"/> with the same <paramref name="search"/> / <paramref name="tsQueryString"/>.
/// </summary>
internal static class DocumentSearchPredicate
{
    public const string TsLanguage = "english";

    /// <summary>
    /// Applies the five OR'd match modes (Name ILIKE, any-version ExtractedText ILIKE, tag-name ILIKE,
    /// party-name ILIKE, and FTS <c>tsvector</c> match) to <paramref name="query"/>. Quote-stripping for the
    /// ILIKE pattern lives here so the list and the palette can't diverge on quoted input.
    /// </summary>
    public static IQueryable<Document> ApplyTextSearch(
        IQueryable<Document> query, string search, string? tsQueryString)
    {
        var namePattern = $"%{search.Replace("\"", string.Empty)}%";
        var hasTs = tsQueryString is not null;
        var tsq = tsQueryString ?? "";
        return query.Where(d =>
            EF.Functions.ILike(d.Name, namePattern)
            || d.Versions.Any(v => v.ExtractedText != null && EF.Functions.ILike(v.ExtractedText, namePattern))
            || d.DocumentTags.Any(dt => EF.Functions.ILike(dt.Tag.Name, namePattern))
            || d.DocumentParties.Any(dp => EF.Functions.ILike(dp.Party.Name, namePattern))
            || (hasTs && d.SearchVector != null
                && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, tsq))));
    }
}
