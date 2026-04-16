using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

public class ReprocessDocumentVersionHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
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

        if (request.TextExtractionEngineId is { } engineId)
        {
            await TextExtractionEngineValidator.ValidateEngineExistsAsync(db, engineId, ct);
        }

        var userId = currentUser.UserId;

        var payload = new ReprocessDocumentVersionPayload(request.DocumentId, request.VersionId, request.TextExtractionEngineId);

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
