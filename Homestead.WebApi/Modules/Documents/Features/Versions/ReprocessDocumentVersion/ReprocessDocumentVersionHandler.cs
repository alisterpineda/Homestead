using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

public class ReprocessDocumentVersionHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    ITextExtractionEngineResolver engineResolver,
    IJobQueue jobQueue,
    IValidator<ReprocessDocumentVersionBody> validator)
{
    /// <param name="body">
    /// Optional. An omitted body has always been legal here and must keep meaning "use the configured default",
    /// not "no engine"; a null <see cref="ReprocessDocumentVersionBody.EngineKey"/> means the same. A non-null key
    /// is validated at enqueue and rejected with a 400 if it is unknown or unavailable — see
    /// ITextExtractionEngineResolver.
    /// </param>
    public async Task<ReprocessDocumentVersionResponse> HandleAsync(Guid documentId, Guid versionId,
        ReprocessDocumentVersionBody? body, CancellationToken ct)
    {
        if (body is not null)
            await validator.ValidateAndThrowAsync(body, ct);

        var documentExists = await db.Documents.AnyAsync(d => d.Id == documentId, ct);
        if (!documentExists)
            throw new EntityNotFoundException("Document not found.");

        var versionExists = await db.DocumentVersions
            .AnyAsync(v => v.Id == versionId && v.DocumentId == documentId, ct);
        if (!versionExists)
            throw new EntityNotFoundException("Document version not found.");

        var userId = currentUser.UserId;

        // Resolved before the first write, so an unusable explicit pick throws BusinessRuleException ->
        // 400 synchronously, before a job row exists. Ordering is legal: the two AnyAsync reads above are
        // deliberately not hooked by CommandTransactionInterceptor, so nothing has opened the transaction
        // yet — jobQueue.EnqueueAsync below is still the first write.
        var engineKey = engineResolver.ResolveKey(body?.EngineKey);

        var payload = new ReprocessDocumentVersionPayload(documentId, versionId, engineKey);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.ReprocessDocumentVersion, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = documentId;
            opts.CreatedByUserId = userId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new ReprocessDocumentVersionResponse { JobId = jobId };
    }
}
