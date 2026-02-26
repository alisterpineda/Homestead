using HomeSuiteHome.WebApi.Authentication;
using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Features.Documents.Processing;
using HomeSuiteHome.WebApi.Persistence;
using HomeSuiteHome.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocumentRevision;

public class CreateDocumentRevisionHandler(
    AppDbContext db,
    IConfiguration configuration,
    ICurrentUserAccessor currentUser,
    IJobQueue jobQueue)
    : ICommandHandler<CreateDocumentRevisionRequest, IResult>
{
    public async Task<IResult> HandleAsync(CreateDocumentRevisionRequest request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            return Results.NotFound();

        var file = request.File;
        var extension = Path.GetExtension(file.FileName);
        var uploadId = Guid.NewGuid();

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");
        var uploadsDir = Path.Combine(mediaPath, DocumentRevision.UploadsSubroot);
        Directory.CreateDirectory(uploadsDir);

        var stagingPath = Path.Combine(uploadsDir, $"{uploadId}{extension}");

        await using (var stream = new FileStream(stagingPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        var userId = currentUser.UserId;

        var payload = new UploadDocumentRevisionPayload(
            uploadId,
            request.DocumentId,
            file.FileName,
            request.Label,
            request.Notes,
            file.ContentType,
            file.Length,
            extension);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.UploadDocumentRevision, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = request.DocumentId;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return Results.Accepted(null, new CreateDocumentRevisionResponse { JobId = jobId });
    }
}
