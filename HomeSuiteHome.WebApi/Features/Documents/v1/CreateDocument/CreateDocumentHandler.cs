using HomeSuiteHome.WebApi.Authentication;
using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Features.Documents.Processing;
using HomeSuiteHome.WebApi.Processing;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentHandler(IConfiguration configuration, ICurrentUserAccessor currentUser, IJobQueue jobQueue)
    : ICommandHandler<CreateDocumentRequest, CreateDocumentResponse>
{
    public async Task<CreateDocumentResponse> HandleAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        var file = request.File;
        var extension = Path.GetExtension(file.FileName);
        var uploadId = Guid.NewGuid();

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");
        var uploadsDir = Path.Combine(mediaPath, Document.UploadsSubroot);
        Directory.CreateDirectory(uploadsDir);

        var stagingPath = Path.Combine(uploadsDir, $"{uploadId}{extension}");

        await using (var stream = new FileStream(stagingPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        var userId = currentUser.User?.Id;

        var payload = new UploadDocumentPayload(
            uploadId,
            file.FileName,
            request.Name,
            file.ContentType,
            file.Length,
            extension);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.UploadDocument, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new CreateDocumentResponse { JobId = jobId };
    }
}
