using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.EmptyTrash;

public class EmptyTrashHandler(AppDbContext db, IConfiguration configuration,
    IAuditLogger auditLogger, IDocumentQueryService accessService,
    ILogger<EmptyTrashHandler> logger)
    : IRequestHandler<EmptyTrashCommand, EmptyTrashResponse>
{
    public async Task<EmptyTrashResponse> Handle(EmptyTrashCommand request, CancellationToken ct)
    {
        var query = db.Documents.IgnoreQueryFilters()
            .Include(d => d.Versions)
            .Where(d => d.DeletedAt != null);

        var documents = await accessService.FilterByWriteAccess(query).ToListAsync(ct);

        if (documents.Count == 0)
            return new EmptyTrashResponse(0);

        var count = await DocumentPermanentDeleter.ExecuteAsync(
            documents, db, configuration, auditLogger, logger, ct);

        return new EmptyTrashResponse(count);
    }
}
