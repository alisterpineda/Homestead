using System.Linq.Expressions;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public class GetDocumentsHandler(AppDbContext db, IDocumentQueryService accessService)
{
    private const string TsLanguage = DocumentSearchTerms.TsLanguage;
    // word_similarity finds the best-matching continuous extent of trigrams within the
    // document name, so it tolerates long names ("Mazda Receipt 2023.pdf") matched by
    // a short search ("mazdo") far better than plain similarity(). 0.3 is below pg_trgm's
    // 0.6 word_similarity default but matches the similarity() default — calibrated so
    // mid-word typos ("invioce" → "invoice", score ≈ 0.33) still surface. Unrelated words
    // ("qqqzzz") score well below 0.1, so noise stays low.
    private const double SuggestionSimilarityThreshold = 0.3;
    private const int SuggestionLimit = 5;
    private const int SuggestionMinSearchLength = 3;

    public async Task<GetDocumentsResult> HandleAsync(
        GetDocumentsQuery request, CancellationToken ct)
    {
        EnsureRangeOrdered(request.DocumentDateFrom, request.DocumentDateTo,
            nameof(GetDocumentsQuery.DocumentDateFrom), nameof(GetDocumentsQuery.DocumentDateTo));
        EnsureRangeOrdered(request.ExpiryDateFrom, request.ExpiryDateTo,
            nameof(GetDocumentsQuery.ExpiryDateFrom), nameof(GetDocumentsQuery.ExpiryDateTo));

        var scopedQuery = await BuildScopedQueryAsync(request, ct);
        var query = scopedQuery;

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : DocumentSearchTerms.Truncate(request.Search.Trim());
        var terms = search is not null
            ? await DocumentSearchTerms.BuildAsync(db, search, request.MatchMode ?? DocumentMatchMode.Substring, ct)
            : null;
        var tsQueryString = terms?.TsQuery;

        if (terms is not null)
        {
            query = query.Where(terms.Any);
        }

        var defaultSort = request.Deleted == true ? DocumentSortBy.DeletedAt : (DocumentSortBy?)null;
        var sortBy = request.SortBy ?? defaultSort;
        // Searching without an explicit sort orders by relevance — when the query left any full-text terms.
        var orderedByRelevance = sortBy is null && tsQueryString is not null;

        var paged = await ApplySort(query, sortBy, request.SortDir, orderedByRelevance ? terms : null)
            .Select(d => new
            {
                d.Id,
                d.Name,
                LatestVersion = d.Versions
                    .OrderByDescending(r => r.VersionNumber)
                    .Select(r => new { r.ContentType, r.FileSize, r.FileExtension })
                    .FirstOrDefault(),
                d.DocumentDate,
                d.ExpiryDate,
                d.CreatedAt,
                d.UpdatedAt,
                d.UpdatedByUserId,
                UpdatedByUserName = d.UpdatedByUser != null ? d.UpdatedByUser.DisplayName : null,
                d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                d.GroupId,
                GroupName = d.Group != null ? d.Group.Name : null,
                d.DeletedAt,
                d.DeletedByUserId,
                DeletedByUserName = d.DeletedByUser != null ? d.DeletedByUser.DisplayName : null,
                VersionCount = d.Versions.Count,
                // The configuration folds accents on both sides, so the headline runs over — and shows — the original
                // text while still finding an accented match.
                MatchSnippet = (tsQueryString != null
                    && d.SearchVector != null
                    && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, tsQueryString)))
                    ? EF.Functions.ToTsQuery(TsLanguage, tsQueryString)
                        .GetResultHeadline(
                            TsLanguage,
                            d.Versions.OrderByDescending(r => r.VersionNumber)
                                .Select(r => r.ExtractedText).FirstOrDefault() ?? string.Empty,
                            MatchSnippetSanitizer.HeadlineOptions)
                    : null,
                Tags = d.DocumentTags.Select(dt => new GetDocumentsTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path,
                    Color = dt.Tag.Color,
                    EffectiveColor = dt.Tag.EffectiveColor
                }).ToList(),
                Parties = d.DocumentParties.Select(dp => new GetDocumentsPartyDto
                {
                    Id = dp.Party.Id,
                    Name = dp.Party.Name
                }).ToList(),
                HasPendingSuggestions = d.Suggestions.Any(s => s.Status == DocumentSuggestionStatus.Pending)
            })
            .Select(d => new GetDocumentsResponse
            {
                Id = d.Id,
                Name = d.Name,
                ContentType = d.LatestVersion != null ? d.LatestVersion.ContentType : "",
                FileSize = d.LatestVersion != null ? d.LatestVersion.FileSize : 0,
                FileExtension = d.LatestVersion != null ? d.LatestVersion.FileExtension : "",
                DocumentDate = d.DocumentDate,
                ExpiryDate = d.ExpiryDate,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                UpdatedByUserId = d.UpdatedByUserId,
                UpdatedByUserName = d.UpdatedByUserName,
                CreatedByUserId = d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUserName,
                OwnerUserId = d.OwnerUserId,
                OwnerUserName = d.OwnerUserName,
                GroupId = d.GroupId,
                GroupName = d.GroupName,
                DeletedAt = d.DeletedAt,
                DeletedByUserId = d.DeletedByUserId,
                DeletedByUserName = d.DeletedByUserName,
                VersionCount = d.VersionCount,
                HasThumbnail = d.LatestVersion != null && ThumbnailService.CanGenerate(d.LatestVersion.ContentType),
                HasPendingSuggestions = d.HasPendingSuggestions,
                MatchSnippet = d.MatchSnippet,
                Tags = d.Tags,
                Parties = d.Parties
            })
            .ToPaginatedAsync(request, ct);

        var matchedOn = terms is not null && paged.Items.Count > 0
            ? await MatchedOnAsync(scopedQuery, terms, paged.Items.Select(i => i.Id).ToList(), ct)
            : null;

        // A row the filter matched but no report branch claims matched the combined vector across fields — its terms
        // split between the name and the text or summary — so it matched both.
        IReadOnlyList<string> acrossFields = [DocumentMatchKind.Name, DocumentMatchKind.Content];
        var sanitizedItems = paged.Items
            .Select(item => item with
            {
                MatchSnippet = MatchSnippetSanitizer.Sanitize(item.MatchSnippet),
                MatchedOn = terms is null ? null : matchedOn?.GetValueOrDefault(item.Id) ?? acrossFields,
            })
            .ToList();

        IReadOnlyList<SearchSuggestion> suggestions = [];
        if (paged.TotalCount == 0 && search is not null && search.Length >= SuggestionMinSearchLength)
        {
            var suggestionInput = search.Replace("\"", " ").Trim();

            var docNames = scopedQuery
                .Where(d => EF.Functions.TrigramsWordSimilarity(suggestionInput, d.Name) >= SuggestionSimilarityThreshold)
                .Select(d => new { Kind = "document", d.Name, Score = EF.Functions.TrigramsWordSimilarity(suggestionInput, d.Name) });

            var tagNames = db.Tags.AsNoTracking()
                .Where(t => t.DocumentTags.Any(dt => scopedQuery.Any(d => d.Id == dt.DocumentId)))
                .Where(t => EF.Functions.TrigramsWordSimilarity(suggestionInput, t.Name) >= SuggestionSimilarityThreshold)
                .Select(t => new { Kind = "tag", t.Name, Score = EF.Functions.TrigramsWordSimilarity(suggestionInput, t.Name) });

            var partyNames = db.Parties.AsNoTracking()
                .Where(p => p.DocumentParties.Any(dp => scopedQuery.Any(d => d.Id == dp.DocumentId)))
                .Where(p => EF.Functions.TrigramsWordSimilarity(suggestionInput, p.Name) >= SuggestionSimilarityThreshold)
                .Select(p => new { Kind = "party", p.Name, Score = EF.Functions.TrigramsWordSimilarity(suggestionInput, p.Name) });

            // One chip per name (a document named like a tag is one suggestion), keeping the best-scoring source's
            // kind; the three sources are small, so the dedupe runs in memory.
            var candidates = await docNames.Concat(tagNames).Concat(partyNames).ToListAsync(ct);
            suggestions = candidates
                .GroupBy(x => x.Name)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(SuggestionLimit)
                .Select(x => new SearchSuggestion(x.Kind, x.Name))
                .ToList();
        }

        return new GetDocumentsResult
        {
            Items = sanitizedItems,
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
            Suggestions = suggestions.Select(s => s.Value).ToList(),
            SuggestionDetails = suggestions,
            OrderedByRelevance = orderedByRelevance,
        };
    }

    // Which branches matched each document of the page: one query, the page's ids against each branch predicate in
    // turn (the same predicates the filter used), so the report can never disagree with the match. Kinds come back
    // in DocumentMatchKind.Order.
    private static async Task<Dictionary<Guid, IReadOnlyList<string>>> MatchedOnAsync(
        IQueryable<Domain.Documents.Document> scopedQuery, DocumentSearchTerms terms, List<Guid> ids, CancellationToken ct)
    {
        var page = scopedQuery.Where(d => ids.Contains(d.Id));
        (string Kind, Expression<Func<Domain.Documents.Document, bool>> Matched)[] branches =
        [
            (DocumentMatchKind.Name, terms.NameReport),
            (DocumentMatchKind.Content, terms.ContentReport),
            (DocumentMatchKind.Tag, terms.Tag),
            (DocumentMatchKind.Party, terms.Party),
            (DocumentMatchKind.Notes, terms.Notes),
        ];
        var union = branches
            .Select(b => page.Where(b.Matched).Select(d => new { d.Id, b.Kind }))
            .Aggregate((all, part) => all.Concat(part));
        var hits = await union.ToListAsync(ct);

        return hits
            .GroupBy(h => h.Id)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(h => h.Kind).Distinct()
                    .OrderBy(k => Array.IndexOf(DocumentMatchKind.Order, k)).ToList());
    }

    private static void EnsureRangeOrdered(DateOnly? from, DateOnly? to, string fromName, string toName)
    {
        if (from is { } f && to is { } t && f > t)
        {
            throw new BusinessRuleException($"{fromName} must be on or before {toName}.");
        }
    }

    private async Task<IQueryable<Domain.Documents.Document>> BuildScopedQueryAsync(
        GetDocumentsQuery request, CancellationToken ct)
    {
        var query = accessService
            .AccessibleDocuments(request.Deleted == true ? TrashFilter.TrashedOnly : TrashFilter.ActiveOnly)
            .AsNoTracking();

        if (request.GroupId.HasValue)
        {
            query = query.Where(d => d.GroupId == request.GroupId.Value);
        }

        // Inclusive bounds; a document with no date (or no expiry) never satisfies a bound.
        if (request.DocumentDateFrom is { } documentDateFrom)
        {
            query = query.Where(d => d.DocumentDate >= documentDateFrom);
        }

        if (request.DocumentDateTo is { } documentDateTo)
        {
            query = query.Where(d => d.DocumentDate <= documentDateTo);
        }

        if (request.ExpiryDateFrom is { } expiryDateFrom)
        {
            query = query.Where(d => d.ExpiryDate >= expiryDateFrom);
        }

        if (request.ExpiryDateTo is { } expiryDateTo)
        {
            query = query.Where(d => d.ExpiryDate <= expiryDateTo);
        }

        if (request.HasDocumentDate is { } hasDocumentDate)
        {
            query = query.Where(d => (d.DocumentDate != null) == hasDocumentDate);
        }

        if (request.HasExpiryDate is { } hasExpiryDate)
        {
            query = query.Where(d => (d.ExpiryDate != null) == hasExpiryDate);
        }

        if (request.HasTags is { } hasTags)
        {
            query = query.Where(d => d.DocumentTags.Any() == hasTags);
        }

        if (request.HasParties is { } hasParties)
        {
            query = query.Where(d => d.DocumentParties.Any() == hasParties);
        }

        if (request.HasPendingSuggestions is { } hasPending)
        {
            // Same predicate as the items' HasPendingSuggestions flag.
            query = query.Where(d =>
                d.Suggestions.Any(s => s.Status == DocumentSuggestionStatus.Pending) == hasPending);
        }

        if (request.PartyIds is { Length: > 0 })
        {
            query = query.Where(d =>
                d.DocumentParties.Any(dp => request.PartyIds.Contains(dp.PartyId)));
        }

        if (request.TagIds is { Length: > 0 })
        {
            if (request.IncludeChildTags)
            {
                var selectedPaths = await db.Tags.AsNoTracking()
                    .Where(t => request.TagIds.Contains(t.Id))
                    .Select(t => t.Path)
                    .ToListAsync(ct);

                var expandedTagIds = db.Tags.AsNoTracking()
                    .Where(t => request.TagIds.Contains(t.Id));

                foreach (var path in selectedPaths)
                {
                    expandedTagIds = expandedTagIds.Union(
                        db.Tags.AsNoTracking().Where(t => t.Path.StartsWith(path + "/")));
                }

                var tagIdList = await expandedTagIds.Select(t => t.Id).ToListAsync(ct);

                query = query.Where(d =>
                    d.DocumentTags.Any(dt => tagIdList.Contains(dt.TagId)));
            }
            else
            {
                query = query.Where(d =>
                    d.DocumentTags.Any(dt => request.TagIds.Contains(dt.TagId)));
            }
        }

        return query;
    }

    // With relevance terms, order by relevance. ILIKE/tag/party-only matches share an identical rank-0 score, so a
    // deterministic UpdatedAt tiebreaker is required — without it Skip/Take pagination over the tied rows is
    // non-deterministic across requests. The universal-search palette (DocumentSearchSource) applies the same
    // tiebreaker; keeping them in lockstep is what makes palette results == list results for the same query. Under
    // Words matching, what the document is (its name, a tag, a party) ranks above what its text mentions.
    private static IOrderedQueryable<Domain.Documents.Document> ApplySort(
        IQueryable<Domain.Documents.Document> query, DocumentSortBy? sortBy, SortDirection? sortDir,
        DocumentSearchTerms? relevance)
    {
        if (relevance is not null)
        {
            var byRank = relevance.Mode == DocumentMatchMode.Words
                ? query.OrderByDescending(relevance.Direct).ThenByDescending(relevance.Rank)
                : query.OrderByDescending(relevance.Rank);
            return byRank.ThenByDescending(d => d.UpdatedAt).ThenBy(d => d.Id);
        }

        var effectiveSortBy = sortBy ?? DocumentSortBy.UpdatedAt;
        var effectiveSortDir = sortDir ?? SortDirection.Desc;

        // Every order ends in UpdatedAt (newest first), then Id, so ties — same name, same date, the undated tail —
        // page deterministically (after an UpdatedAt sort the extra UpdatedAt key is a harmless no-op). Name sorts
        // case-insensitively (lower(), which the trigram index does not serve, but a household's document table is
        // small). Documents without a date (or an expiry) sort last in both directions: Postgres would otherwise put
        // NULLs first under DESC, and most documents have no expiry.
        var ordered = (effectiveSortBy, effectiveSortDir) switch
        {
            (DocumentSortBy.Name, SortDirection.Asc) => query.OrderBy(d => d.Name.ToLower()),
            (DocumentSortBy.Name, SortDirection.Desc) => query.OrderByDescending(d => d.Name.ToLower()),
            (DocumentSortBy.FileSize, SortDirection.Asc) => query.OrderBy(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.FileSize).FirstOrDefault()),
            (DocumentSortBy.FileSize, SortDirection.Desc) => query.OrderByDescending(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.FileSize).FirstOrDefault()),
            (DocumentSortBy.ContentType, SortDirection.Asc) => query.OrderBy(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ContentType).FirstOrDefault()),
            (DocumentSortBy.ContentType, SortDirection.Desc) => query.OrderByDescending(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ContentType).FirstOrDefault()),
            (DocumentSortBy.DocumentDate, SortDirection.Asc) => query
                .OrderBy(d => d.DocumentDate == null)
                .ThenBy(d => d.DocumentDate),
            (DocumentSortBy.DocumentDate, SortDirection.Desc) => query
                .OrderBy(d => d.DocumentDate == null)
                .ThenByDescending(d => d.DocumentDate),
            (DocumentSortBy.ExpiryDate, SortDirection.Asc) => query
                .OrderBy(d => d.ExpiryDate == null)
                .ThenBy(d => d.ExpiryDate),
            (DocumentSortBy.ExpiryDate, SortDirection.Desc) => query
                .OrderBy(d => d.ExpiryDate == null)
                .ThenByDescending(d => d.ExpiryDate),
            (DocumentSortBy.CreatedAt, SortDirection.Asc) => query.OrderBy(d => d.CreatedAt),
            (DocumentSortBy.CreatedAt, SortDirection.Desc) => query.OrderByDescending(d => d.CreatedAt),
            (DocumentSortBy.UpdatedAt, SortDirection.Asc) => query.OrderBy(d => d.UpdatedAt),
            (DocumentSortBy.DeletedAt, SortDirection.Asc) => query.OrderBy(d => d.DeletedAt),
            (DocumentSortBy.DeletedAt, SortDirection.Desc) => query.OrderByDescending(d => d.DeletedAt),
            _ => query.OrderByDescending(d => d.UpdatedAt),
        };

        return ordered.ThenByDescending(d => d.UpdatedAt).ThenBy(d => d.Id);
    }
}
