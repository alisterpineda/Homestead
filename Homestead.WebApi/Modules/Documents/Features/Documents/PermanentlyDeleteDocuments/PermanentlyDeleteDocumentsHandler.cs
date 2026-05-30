using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PermanentlyDeleteDocuments;

public class PermanentlyDeleteDocumentsHandler(AppDbContext db, IConfiguration configuration,
    IAuditContext audit, IUnitOfWork unitOfWork, IDocumentQueryService accessService,
    ILogger<PermanentlyDeleteDocumentsHandler> logger)
    : IRequestHandler<PermanentlyDeleteDocumentsCommand, PermanentlyDeleteDocumentsResponse>
{
    public async Task<PermanentlyDeleteDocumentsResponse> Handle(PermanentlyDeleteDocumentsCommand request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Include(d => d.Versions)
            .Where(d => d.DeletedAt != null && request.Ids.Contains(d.Id));

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count != request.Ids.Count)
            throw new AccessDeniedException();

        if (documents.Count == 0)
            return new PermanentlyDeleteDocumentsResponse(0);

        var count = await DocumentPermanentDeleter.ExecuteAsync(
            documents, db, configuration, audit, unitOfWork, logger, ct);

        return new PermanentlyDeleteDocumentsResponse(count);
    }
}
