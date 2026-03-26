using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Export;

public class GetExportsHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetExportsQuery, PaginatedResponse<GetExportsResponse>>
{
    public async Task<PaginatedResponse<GetExportsResponse>> Handle(
        GetExportsQuery request, CancellationToken ct)
    {
        var query = db.ProcessingJobs
            .AsNoTracking()
            .Where(j => j.Type == ProcessingJobType.ExportDocuments)
            .Where(j => j.CreatedByUserId == currentUser.UserId)
            .OrderByDescending(j => j.CreatedAt);

        var paginated = await query
            .Select(j => new
            {
                j.Id,
                j.Status,
                j.Error,
                j.CreatedAt,
                j.CompletedAt,
                j.Payload
            })
            .ToPaginatedAsync(request, ct);

        var items = paginated.Items.Select(j =>
        {
            int? documentCount = null;
            long? zipFileSize = null;
            DateTimeOffset? expiresAt = null;

            if (j.Status == ProcessingJobStatus.Completed && j.Payload is not null)
            {
                var result = JsonSerializer.Deserialize<ExportResultPayload>(j.Payload);
                if (result is not null)
                {
                    documentCount = result.DocumentCount;
                    zipFileSize = result.ZipFileSize;
                    expiresAt = result.ExpiresAt;
                }
            }

            return new GetExportsResponse
            {
                Id = j.Id,
                Status = j.Status,
                Error = j.Error,
                CreatedAt = j.CreatedAt,
                CompletedAt = j.CompletedAt,
                DocumentCount = documentCount,
                ZipFileSize = zipFileSize,
                ExpiresAt = expiresAt
            };
        }).ToList();

        return new PaginatedResponse<GetExportsResponse>
        {
            Items = items,
            Page = paginated.Page,
            PageSize = paginated.PageSize,
            TotalCount = paginated.TotalCount
        };
    }
}
