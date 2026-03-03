using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentHandler(IConfiguration configuration, ICurrentUserAccessor currentUser, IJobQueue jobQueue,
    AppDbContext db, IDocumentAccessService accessService)
    : ICommandHandler<CreateDocumentRequest, IResult>
{
    public async Task<IResult> HandleAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        // Validate GroupId if provided
        if (request.GroupId.HasValue)
        {
            var groupExists = await db.Groups.AnyAsync(g => g.Id == request.GroupId.Value, ct);
            if (!groupExists)
                return Results.BadRequest(new { Error = "Group not found." });

            var canWrite = await accessService.CanWriteToGroupAsync(request.GroupId.Value, ct);
            if (!canWrite)
                return Results.Forbid();
        }

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

        var payload = new UploadDocumentPayload(
            uploadId,
            file.FileName,
            request.Name,
            request.Label,
            file.ContentType,
            file.Length,
            extension,
            request.GroupId);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.UploadDocument, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return Results.Accepted(null, new CreateDocumentResponse { JobId = jobId });
    }
}
