using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocumentVersion;

public class CreateDocumentVersionHandler(
    AppDbContext db,
    IConfiguration configuration,
    ICurrentUserAccessor currentUser,
    IJobQueue jobQueue,
    IDocumentAccessService accessService)
    : ICommandHandler<CreateDocumentVersionRequest, IResult>
{
    public async Task<IResult> HandleAsync(CreateDocumentVersionRequest request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            return Results.NotFound();

        var access = await accessService.CheckAccessAsync(request.DocumentId, DocumentOperation.Write, ct);
        if (!access.IsAllowed)
            return Results.Forbid();

        var file = request.File;
        var extension = Path.GetExtension(file.FileName);
        var uploadId = Guid.NewGuid();

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");
        var uploadsDir = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot);
        Directory.CreateDirectory(uploadsDir);

        var stagingPath = Path.Combine(uploadsDir, $"{uploadId}{extension}");

        await using (var stream = new FileStream(stagingPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        var userId = currentUser.UserId;

        var payload = new UploadDocumentVersionPayload(
            uploadId,
            request.DocumentId,
            file.FileName,
            request.Label,
            file.ContentType,
            file.Length,
            extension);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.UploadDocumentVersion, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = request.DocumentId;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return Results.Accepted(null, new CreateDocumentVersionResponse { JobId = jobId });
    }
}
