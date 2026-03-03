using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocument;

public class GetDocumentHandler(AppDbContext db, IDocumentAccessService accessService)
    : IQueryHandler<GetDocumentRequest, GetDocumentResponse?>
{
    public async Task<GetDocumentResponse?> HandleAsync(GetDocumentRequest request, CancellationToken ct)
    {
        var access = await accessService.CheckAccessAsync(request.Id, DocumentOperation.Read, ct);
        if (!access.IsAllowed)
            throw new ForbiddenAccessException();

        return await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.CreatedAt,
                d.DocumentDate,
                d.ValidFrom,
                d.ValidUntil,
                d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                d.GroupId,
                GroupName = d.Group != null ? d.Group.Name : null,
                LatestVersion = d.Versions
                    .OrderByDescending(r => r.VersionNumber)
                    .Select(r => new
                    {
                        r.ContentType,
                        r.FileSize,
                        r.FileHash,
                        r.FileExtension,
                        r.ExtractedText
                    })
                    .FirstOrDefault(),
                Versions = d.Versions
                    .OrderByDescending(r => r.VersionNumber)
                    .Select(r => new GetDocumentVersionDto
                    {
                        Id = r.Id,
                        VersionNumber = r.VersionNumber,
                        Label = r.Label,
                        ContentType = r.ContentType,
                        FileSize = r.FileSize,
                        FileExtension = r.FileExtension,
                        CreatedAt = r.CreatedAt
                    })
                    .ToList(),
                VersionCount = d.Versions.Count,
                Tags = d.DocumentTags.Select(dt => new GetDocumentTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path,
                    Color = dt.Tag.Color
                }).ToList(),
                Parties = d.DocumentParties.Select(dp => new GetDocumentPartyDto
                {
                    Id = dp.Party.Id,
                    Name = dp.Party.Name,
                    Description = dp.Party.Description
                }).ToList()
            })
            .Select(d => d.LatestVersion == null ? null : new GetDocumentResponse
            {
                Id = d.Id,
                Name = d.Name,
                ContentType = d.LatestVersion.ContentType,
                FileSize = d.LatestVersion.FileSize,
                FileHash = d.LatestVersion.FileHash,
                FileExtension = d.LatestVersion.FileExtension,
                CreatedAt = d.CreatedAt,
                DocumentDate = d.DocumentDate,
                ValidFrom = d.ValidFrom,
                ValidUntil = d.ValidUntil,
                CreatedByUserId = d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUserName,
                OwnerUserId = d.OwnerUserId,
                OwnerUserName = d.OwnerUserName,
                GroupId = d.GroupId,
                GroupName = d.GroupName,
                ExtractedText = d.LatestVersion.ExtractedText,
                VersionCount = d.VersionCount,
                Versions = d.Versions,
                Tags = d.Tags,
                Parties = d.Parties
            })
            .FirstOrDefaultAsync(ct);
    }
}
