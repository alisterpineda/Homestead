using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Processing;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public class ClassifyDocumentHandler(IJobQueue jobQueue, ICurrentUserAccessor currentUser)
    : IRequestHandler<ClassifyDocumentCommand, ClassifyDocumentResponse>
{
    public async Task<ClassifyDocumentResponse> Handle(ClassifyDocumentCommand request, CancellationToken ct)
    {
        var payload = new ClassifyDocumentPayload(request.DocumentId, request.AiProviderId);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.ClassifyDocument, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = request.DocumentId;
            opts.CreatedByUserId = currentUser.UserId;
            opts.Payload = JsonSerializer.Serialize(payload);
        }, ct);

        return new ClassifyDocumentResponse(jobId);
    }
}
