using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PermanentlyDeleteDocuments;

public class PermanentlyDeleteDocumentsHandler(AppDbContext db, IOptions<FileStorageOptions> storage,
    IAuditContext audit, IUnitOfWork unitOfWork, IDocumentQueryService accessService,
    ILogger<PermanentlyDeleteDocumentsHandler> logger)
{
    public async Task<PermanentlyDeleteDocumentsResponse> HandleAsync(PermanentlyDeleteDocumentsCommand request, CancellationToken ct)
    {
        var documents = await accessService.WritableDocuments(TrashFilter.TrashedOnly)
            .Include(d => d.Versions)
            .Where(d => request.Ids.Contains(d.Id))
            .ToListAsync(ct);

        if (documents.Count != request.Ids.Count)
            throw new AccessDeniedException();

        if (documents.Count == 0)
            return new PermanentlyDeleteDocumentsResponse(0);

        var count = await DocumentPermanentDeleter.ExecuteAsync(
            documents, db, storage.Value, audit, unitOfWork, logger, ct);

        return new PermanentlyDeleteDocumentsResponse(count);
    }
}
