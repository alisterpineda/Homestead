using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Features.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.RestoreDocuments;

public class RestoreDocumentsHandler(AppDbContext db,
    IAuditContext audit, IDocumentQueryService accessService)
    : IRequestHandler<RestoreDocumentsCommand, RestoreDocumentsResponse>
{
    public async Task<RestoreDocumentsResponse> Handle(RestoreDocumentsCommand request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && request.Ids.Contains(d.Id));

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count != request.Ids.Count)
            throw new AccessDeniedException();

        foreach (var doc in documents)
        {
            doc.DeletedAt = null;
            doc.DeletedByUserId = null;
            audit.Restored("Document", doc.Id, description: doc.Name);
        }

        await db.SaveChangesAsync(ct);

        return new RestoreDocumentsResponse(documents.Count);
    }
}
