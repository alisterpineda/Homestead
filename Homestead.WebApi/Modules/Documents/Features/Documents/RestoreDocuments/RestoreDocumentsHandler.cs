using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.RestoreDocuments;

public class RestoreDocumentsHandler(AppDbContext db,
    IAuditContext audit, IDocumentQueryService accessService,
    ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
    : IRequestHandler<RestoreDocumentsCommand, RestoreDocumentsResponse>
{
    public async Task<RestoreDocumentsResponse> Handle(RestoreDocumentsCommand request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && request.Ids.Contains(d.Id));

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count != request.Ids.Count)
            throw new AccessDeniedException();

        var now = DateTimeOffset.UtcNow;
        foreach (var doc in documents)
        {
            doc.DeletedAt = null;
            doc.DeletedByUserId = null;
            doc.UpdatedAt = now;
            doc.UpdatedByUserId = currentUser.UserId;
            audit.Restored("Document", doc.Id, description: doc.Name);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new RestoreDocumentsResponse(documents.Count);
    }
}
