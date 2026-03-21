using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PermanentlyDeleteDocuments;

public class PermanentlyDeleteDocumentsHandler(AppDbContext db, IConfiguration configuration,
    ICurrentUserAccessor currentUser, IAuditLogger auditLogger, IDocumentQueryService accessService,
    ILogger<PermanentlyDeleteDocumentsHandler> logger)
    : ICommandHandler<PermanentlyDeleteDocumentsRequest, PermanentlyDeleteDocumentsResponse>
{
    public async Task<PermanentlyDeleteDocumentsResponse> HandleAsync(PermanentlyDeleteDocumentsRequest request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Include(d => d.Versions)
            .Where(d => d.DeletedAt != null && request.Ids.Contains(d.Id));

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count != request.Ids.Count)
            throw new ForbiddenAccessException();

        if (documents.Count == 0)
            return new PermanentlyDeleteDocumentsResponse(0);

        var count = await DocumentPermanentDeleter.ExecuteAsync(
            documents, db, configuration, currentUser, auditLogger, logger, ct);

        return new PermanentlyDeleteDocumentsResponse(count);
    }
}
