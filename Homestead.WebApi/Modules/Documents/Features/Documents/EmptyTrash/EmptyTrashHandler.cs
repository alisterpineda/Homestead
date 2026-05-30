using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.EmptyTrash;

public class EmptyTrashHandler(AppDbContext db, IConfiguration configuration,
    IAuditContext audit, IUnitOfWork unitOfWork, IDocumentQueryService accessService,
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
            documents, db, configuration, audit, unitOfWork, logger, ct);

        return new EmptyTrashResponse(count);
    }
}
