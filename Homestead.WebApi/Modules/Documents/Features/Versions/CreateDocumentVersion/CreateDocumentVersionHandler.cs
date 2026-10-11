using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.CreateDocumentVersion;

public class CreateDocumentVersionHandler(
    AppDbContext db,
    IOptions<FileStorageOptions> storage,
    ICurrentUserAccessor currentUser,
    IJobQueue jobQueue)
{
    public async Task<CreateDocumentVersionResponse> HandleAsync(Guid documentId, string? label, IFormFile file,
        CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == documentId, ct);
        if (!documentExists)
            throw new EntityNotFoundException("Document not found.");

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

        var payload = new UploadDocumentVersionPayload(
            uploadId,
            documentId,
            file.FileName,
            label,
            file.ContentType,
            file.Length,
            extension);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.UploadDocumentVersion, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = documentId;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new CreateDocumentVersionResponse { JobId = jobId };
    }
}
