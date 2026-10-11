using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.CreateDocument;

public class CreateDocumentHandler(IOptions<FileStorageOptions> storage, ICurrentUserAccessor currentUser, IJobQueue jobQueue,
    AppDbContext db, IDocumentQueryService accessService)
{
    public async Task<CreateDocumentResponse> HandleAsync(string? name, string? label, Guid? groupId, IFormFile file,
        CancellationToken ct)
    {
        // Validate GroupId if provided
        if (groupId.HasValue)
        {
            var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId.Value, ct);
            if (!groupExists)
                throw new BusinessRuleException("Group not found.");

            var canWrite = await accessService.CanWriteToGroupAsync(groupId.Value, ct);
            if (!canWrite)
                throw new AccessDeniedException();
        }

        var extension = Path.GetExtension(file.FileName);
        var uploadId = Guid.NewGuid();

        var mediaPath = storage.Value.MediaPath;
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
            name,
            label,
            file.ContentType,
            file.Length,
            extension,
            groupId);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.UploadDocument, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new CreateDocumentResponse { JobId = jobId };
    }
}
