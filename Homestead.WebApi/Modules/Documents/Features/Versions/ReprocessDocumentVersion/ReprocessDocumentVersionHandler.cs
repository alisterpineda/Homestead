using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

public class ReprocessDocumentVersionHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    ITextExtractionEngineResolver engineResolver,
    IJobQueue jobQueue)
    : IRequestHandler<ReprocessDocumentVersionCommand, ReprocessDocumentVersionResponse>
{
    public async Task<ReprocessDocumentVersionResponse> Handle(ReprocessDocumentVersionCommand request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            throw new EntityNotFoundException("Document not found.");

        var versionExists = await db.DocumentVersions
            .AnyAsync(v => v.Id == request.VersionId && v.DocumentId == request.DocumentId, ct);
        if (!versionExists)
            throw new EntityNotFoundException("Document version not found.");

        var userId = currentUser.UserId;

        // Resolved before the first write, so an unusable explicit pick throws BusinessRuleException ->
        // 400 synchronously, before a job row exists. Ordering is legal: the two AnyAsync reads above are
        // deliberately not hooked by CommandTransactionInterceptor, so nothing has opened the transaction
        // yet — jobQueue.EnqueueAsync below is still the first write.
        var engineKey = engineResolver.ResolveKey(request.EngineKey);

        var payload = new ReprocessDocumentVersionPayload(request.DocumentId, request.VersionId, engineKey);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.ReprocessDocumentVersion, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = request.DocumentId;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new ReprocessDocumentVersionResponse { JobId = jobId };
    }
}
