using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.RestoreDocuments;

public class RestoreDocumentsHandler(AppDbContext db, ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger, IDocumentQueryService accessService)
    : ICommandHandler<RestoreDocumentsRequest, RestoreDocumentsResponse>
{
    public async Task<RestoreDocumentsResponse> HandleAsync(RestoreDocumentsRequest request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && request.Ids.Contains(d.Id));

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count != request.Ids.Count)
            throw new ForbiddenAccessException();

        foreach (var doc in documents)
        {
            doc.DeletedAt = null;
            doc.DeletedByUserId = null;
            auditLogger.LogRestored("Document", doc.Id, currentUser.UserId, description: doc.Name);
        }

        await db.SaveChangesAsync(ct);

        return new RestoreDocumentsResponse(documents.Count);
    }
}
