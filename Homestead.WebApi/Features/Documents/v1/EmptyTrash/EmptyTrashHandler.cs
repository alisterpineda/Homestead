using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.EmptyTrash;

public class EmptyTrashHandler(AppDbContext db, IConfiguration configuration,
    ICurrentUserAccessor currentUser, IAuditLogger auditLogger, IDocumentQueryService accessService)
    : ICommandHandler<EmptyTrashRequest, EmptyTrashResponse>
{
    public async Task<EmptyTrashResponse> HandleAsync(EmptyTrashRequest request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Include(d => d.Versions)
            .Where(d => d.DeletedAt != null);

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count == 0)
            return new EmptyTrashResponse(0);

        var count = await DocumentPermanentDeleter.ExecuteAsync(
            documents, db, configuration, currentUser, auditLogger, ct);

        return new EmptyTrashResponse(count);
    }
}
