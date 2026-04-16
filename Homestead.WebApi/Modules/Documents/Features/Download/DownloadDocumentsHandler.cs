using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public class DownloadDocumentsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IDocumentQueryService accessService,
    IJobQueue jobQueue) : IRequestHandler<DownloadDocumentsCommand, DownloadDocumentsResponse>
{
    public async Task<DownloadDocumentsResponse> Handle(DownloadDocumentsCommand request, CancellationToken ct)
    {
        // Guard: only one active download per user
        var hasActiveDownload = await db.ProcessingJobs
            .AnyAsync(j =>
                j.Type == ProcessingJobType.DownloadDocuments &&
                j.CreatedByUserId == currentUser.UserId &&
                (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.InProgress), ct);

        if (hasActiveDownload)
            throw new BusinessRuleException("A download is already in progress.");

        List<Guid> documentIds;

        if (request.Ids is { Count: > 0 })
        {
            var requestedIds = request.Ids;
            await accessService.EnsureAllDocumentsAccessibleAsync(
                db.Documents.Where(d => requestedIds.Contains(d.Id)).Select(d => d.Id), ct);
            documentIds = requestedIds;
        }
        else
        {
            documentIds = await accessService.AccessibleDocumentIds().ToListAsync(ct);

            if (documentIds.Count == 0)
                throw new BusinessRuleException("No documents available to download.");
        }

        var payload = new DownloadDocumentsPayload(documentIds);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.DownloadDocuments, opts =>
        {
            opts.EntityType = ProcessingEntityType.Download;
            opts.CreatedByUserId = currentUser.UserId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new DownloadDocumentsResponse(jobId);
    }
}
