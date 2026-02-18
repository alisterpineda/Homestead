using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocument;

public class GetDocumentHandler(AppDbContext db)
    : IQueryHandler<GetDocumentRequest, GetDocumentResponse?>
{
    public async Task<GetDocumentResponse?> HandleAsync(GetDocumentRequest request, CancellationToken ct)
    {
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => new GetDocumentResponse
            {
                Id = d.Id,
                Name = d.Name,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSize = d.FileSize,
                FileHash = d.FileHash,
                CreatedAt = d.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
    }
}
