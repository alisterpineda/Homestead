using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.ReprocessDocumentVersion;

public class ReprocessDocumentVersionHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IJobQueue jobQueue,
    IAuthorizationService authService,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ReprocessDocumentVersionRequest, ReprocessDocumentVersionResponse>
{
    public async Task<ReprocessDocumentVersionResponse> Handle(ReprocessDocumentVersionRequest request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            throw new NotFoundException("Document not found.");

        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Write);

        var versionExists = await db.DocumentVersions
            .AnyAsync(v => v.Id == request.VersionId && v.DocumentId == request.DocumentId, ct);
        if (!versionExists)
            throw new NotFoundException("Document version not found.");

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
