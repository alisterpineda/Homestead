using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocuments;

public class GetDocumentsHandler(AppDbContext db, IDocumentAccessService accessService)
    : IQueryHandler<GetDocumentsRequest, PaginatedResponse<GetDocumentsResponse>>
{
    public async Task<PaginatedResponse<GetDocumentsResponse>> HandleAsync(
        GetDocumentsRequest request, CancellationToken ct)
    {
        var query = accessService.FilterByAccess(db.Documents.AsNoTracking());

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

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        if (search is not null)
        {
            var namePattern = $"%{search}%";
            query = query.Where(d =>
                EF.Functions.ILike(d.Name, namePattern) ||
                (d.SearchVector != null &&
                 d.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", search))));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await ApplySort(query, request.SortBy, request.SortDir, search)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(d => new
            {
                d.Id,
                d.Name,
                LatestVersion = d.Versions
                    .OrderByDescending(r => r.VersionNumber)
                    .Select(r => new { r.ContentType, r.FileSize, r.FileExtension })
                    .FirstOrDefault(),
                d.DocumentDate,
                d.ValidFrom,
                d.ValidUntil,
                d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                d.GroupId,
                GroupName = d.Group != null ? d.Group.Name : null,
                VersionCount = d.Versions.Count,
                Tags = d.DocumentTags.Select(dt => new GetDocumentsTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path,
                    Color = dt.Tag.Color
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
                ValidFrom = d.ValidFrom,
                ValidUntil = d.ValidUntil,
                CreatedByUserId = d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUserName,
                OwnerUserId = d.OwnerUserId,
                OwnerUserName = d.OwnerUserName,
                GroupId = d.GroupId,
                GroupName = d.GroupName,
                VersionCount = d.VersionCount,
                Tags = d.Tags,
                Parties = d.Parties
            })
            .ToListAsync(ct);

        return new PaginatedResponse<GetDocumentsResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    private static IOrderedQueryable<Domains.Documents.Document> ApplySort(
        IQueryable<Domains.Documents.Document> query, DocumentSortBy? sortBy, SortDirection? sortDir,
        string? search = null)
    {
        // When searching without an explicit sort, order by relevance
        if (sortBy is null && search is not null)
        {
            return query.OrderByDescending(d =>
                d.SearchVector!.RankCoverDensity(EF.Functions.WebSearchToTsQuery("english", search)));
        }

        var effectiveSortBy = sortBy ?? DocumentSortBy.Name;
        var effectiveSortDir = sortDir ?? SortDirection.Asc;

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
            _ => query.OrderBy(d => d.Name),
        };
    }
}
