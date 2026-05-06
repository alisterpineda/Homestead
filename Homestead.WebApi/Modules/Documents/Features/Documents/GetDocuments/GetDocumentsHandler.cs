using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public class GetDocumentsHandler(AppDbContext db, IDocumentQueryService accessService)
    : IRequestHandler<GetDocumentsQuery, GetDocumentsResult>
{
    private const string TsLanguage = "english";
    // word_similarity finds the best-matching continuous extent of trigrams within the
    // document name, so it tolerates long names ("Mazda Receipt 2023.pdf") matched by
    // a short search ("mazdo") far better than plain similarity(). 0.3 is below pg_trgm's
    // 0.6 word_similarity default but matches the similarity() default — calibrated so
    // mid-word typos ("invioce" → "invoice", score ≈ 0.33) still surface. Unrelated words
    // ("qqqzzz") score well below 0.1, so noise stays low.
    private const double SuggestionSimilarityThreshold = 0.3;
    private const int SuggestionLimit = 5;
    private const int SuggestionMinSearchLength = 3;

    public async Task<GetDocumentsResult> Handle(
        GetDocumentsQuery request, CancellationToken ct)
    {
        var scopedQuery = await BuildScopedQueryAsync(request, ct);
        var query = scopedQuery;

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        string? tsQueryString = search is not null ? SearchTsQueryBuilder.Build(search) : null;
        var ilikeSearch = search?.Replace("\"", string.Empty);

        if (search is not null)
        {
            var namePattern = $"%{ilikeSearch}%";
            var hasTsQuery = tsQueryString is not null;
            var tsq = tsQueryString ?? "";

            query = query.Where(d =>
                EF.Functions.ILike(d.Name, namePattern) ||
                d.Versions.Any(v => v.ExtractedText != null
                    && EF.Functions.ILike(v.ExtractedText, namePattern)) ||
                d.DocumentTags.Any(dt => EF.Functions.ILike(dt.Tag.Name, namePattern)) ||
                d.DocumentParties.Any(dp => EF.Functions.ILike(dp.Party.Name, namePattern)) ||
                (hasTsQuery && d.SearchVector != null &&
                 d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, tsq))));
        }

        var defaultSort = request.Deleted == true ? DocumentSortBy.DeletedAt : (DocumentSortBy?)null;

        var paged = await ApplySort(query, request.SortBy ?? defaultSort, request.SortDir, tsQueryString)
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
                }).ToList()
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
                MatchSnippet = d.MatchSnippet,
                Tags = d.Tags,
                Parties = d.Parties
            })
            .ToPaginatedAsync(request, ct);

        var sanitizedItems = paged.Items
            .Select(item => item with { MatchSnippet = MatchSnippetSanitizer.Sanitize(item.MatchSnippet) })
            .ToList();

        IReadOnlyList<string> suggestions = [];
        if (paged.TotalCount == 0 && search is not null && search.Length >= SuggestionMinSearchLength)
        {
            var suggestionInput = search.Replace("\"", " ").Trim();

            var docNames = scopedQuery
                .Where(d => EF.Functions.TrigramsWordSimilarity(suggestionInput, d.Name) >= SuggestionSimilarityThreshold)
                .Select(d => new { d.Name, Score = EF.Functions.TrigramsWordSimilarity(suggestionInput, d.Name) });

            var tagNames = db.Tags.AsNoTracking()
                .Where(t => t.DocumentTags.Any(dt => scopedQuery.Any(d => d.Id == dt.DocumentId)))
                .Where(t => EF.Functions.TrigramsWordSimilarity(suggestionInput, t.Name) >= SuggestionSimilarityThreshold)
                .Select(t => new { t.Name, Score = EF.Functions.TrigramsWordSimilarity(suggestionInput, t.Name) });

            var partyNames = db.Parties.AsNoTracking()
                .Where(p => p.DocumentParties.Any(dp => scopedQuery.Any(d => d.Id == dp.DocumentId)))
                .Where(p => EF.Functions.TrigramsWordSimilarity(suggestionInput, p.Name) >= SuggestionSimilarityThreshold)
                .Select(p => new { p.Name, Score = EF.Functions.TrigramsWordSimilarity(suggestionInput, p.Name) });

            suggestions = await docNames
                .Concat(tagNames)
                .Concat(partyNames)
                .GroupBy(x => x.Name)
                .Select(g => new { Name = g.Key, Score = g.Max(x => x.Score) })
                .OrderByDescending(x => x.Score)
                .Take(SuggestionLimit)
                .Select(x => x.Name)
                .ToListAsync(ct);
        }

        return new GetDocumentsResult
        {
            Items = sanitizedItems,
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
            Suggestions = suggestions
        };
    }

    private async Task<IQueryable<Domain.Documents.Document>> BuildScopedQueryAsync(
        GetDocumentsQuery request, CancellationToken ct)
    {
        IQueryable<Domain.Documents.Document> query;

        if (request.Deleted == true)
        {
            query = accessService.FilterByAccess(
                db.Documents.IgnoreQueryFilters().AsNoTracking().Where(d => d.DeletedAt != null));
        }
        else
        {
            query = accessService.FilterByAccess(db.Documents.AsNoTracking());
        }

        if (request.GroupId.HasValue)
        {
            query = query.Where(d => d.GroupId == request.GroupId.Value);
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

    private static IOrderedQueryable<Domain.Documents.Document> ApplySort(
        IQueryable<Domain.Documents.Document> query, DocumentSortBy? sortBy, SortDirection? sortDir,
        string? tsQueryString = null)
    {
        // When searching without an explicit sort, order by relevance
        if (sortBy is null && tsQueryString is not null)
        {
            var tsq = tsQueryString;
            return query.OrderByDescending(d =>
                d.SearchVector!.RankCoverDensity(EF.Functions.ToTsQuery(TsLanguage, tsq)));
        }

        var effectiveSortBy = sortBy ?? DocumentSortBy.UpdatedAt;
        var effectiveSortDir = sortDir ?? SortDirection.Desc;

        return (effectiveSortBy, effectiveSortDir) switch
        {
            (DocumentSortBy.Name, SortDirection.Asc) => query.OrderBy(d => d.Name),
            (DocumentSortBy.Name, SortDirection.Desc) => query.OrderByDescending(d => d.Name),
            (DocumentSortBy.FileSize, SortDirection.Asc) => query.OrderBy(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.FileSize).FirstOrDefault()),
            (DocumentSortBy.FileSize, SortDirection.Desc) => query.OrderByDescending(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.FileSize).FirstOrDefault()),
            (DocumentSortBy.ContentType, SortDirection.Asc) => query.OrderBy(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ContentType).FirstOrDefault()),
            (DocumentSortBy.ContentType, SortDirection.Desc) => query.OrderByDescending(d =>
                d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ContentType).FirstOrDefault()),
            (DocumentSortBy.DocumentDate, SortDirection.Asc) => query.OrderBy(d => d.DocumentDate),
            (DocumentSortBy.DocumentDate, SortDirection.Desc) => query.OrderByDescending(d => d.DocumentDate),
            (DocumentSortBy.CreatedAt, SortDirection.Asc) => query.OrderBy(d => d.CreatedAt),
            (DocumentSortBy.CreatedAt, SortDirection.Desc) => query.OrderByDescending(d => d.CreatedAt),
            (DocumentSortBy.UpdatedAt, SortDirection.Asc) => query.OrderBy(d => d.UpdatedAt),
            (DocumentSortBy.UpdatedAt, SortDirection.Desc) => query.OrderByDescending(d => d.UpdatedAt),
            (DocumentSortBy.DeletedAt, SortDirection.Asc) => query.OrderBy(d => d.DeletedAt),
            (DocumentSortBy.DeletedAt, SortDirection.Desc) => query.OrderByDescending(d => d.DeletedAt),
            _ => query.OrderByDescending(d => d.UpdatedAt),
        };
    }
}
