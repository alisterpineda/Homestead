using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocuments;

public class GetDocumentsHandler(AppDbContext db)
    : IQueryHandler<GetDocumentsRequest, PaginatedResponse<GetDocumentsResponse>>
{
    public async Task<PaginatedResponse<GetDocumentsResponse>> HandleAsync(
        GetDocumentsRequest request, CancellationToken ct)
    {
        var query = db.Documents.AsNoTracking();

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

        var totalCount = await query.CountAsync(ct);

        var items = await ApplySort(query, request.SortBy ?? DocumentSortBy.Name, request.SortDir ?? SortDirection.Asc)
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
                VersionCount = d.Versions.Count,
                Tags = d.DocumentTags.Select(dt => new GetDocumentsTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path
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
        IQueryable<Domains.Documents.Document> query, DocumentSortBy sortBy, SortDirection sortDir)
    {
        return (sortBy, sortDir) switch
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
