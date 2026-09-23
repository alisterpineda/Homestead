using System.Text.Json;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ClassifyDocumentJobHandler(
    AppDbContext db,
    IDocumentClassifier classifier,
    IProcessingNotifier notifier,
    ISender sender) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ClassifyDocument;

    /// <summary>
    /// The model call is the one part of classification that fails for reasons that pass: a provider
    /// restarting, a model still loading, a rate limit the SDK's own retries gave up on. Safe to re-run:
    /// <see cref="ApplyClassificationCommand"/> reverts any earlier AI suggestions before applying new ones.
    /// </summary>
    public int MaxAttempts => 3;

    /// <summary>
    /// The transient-infrastructure default, plus malformed model output — the same prompt usually parses
    /// on the next sample. A missing document or payload stays final.
    /// </summary>
    public bool ShouldRetry(Exception exception) =>
        exception is JsonException || JobFailureClassifier.IsTransient(exception);

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        // A payload that won't parse fails the same way every time, so it must not surface as the
        // JsonException that ShouldRetry reads as malformed model output.
        ClassifyDocumentPayload? payload;
        try
        {
            payload = job.GetPayload<ClassifyDocumentPayload>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Job payload is missing or invalid.", ex);
        }

        if (payload is null)
            throw new InvalidOperationException("Job payload is missing or invalid.");

        job.EntityId = payload.DocumentId;

        // Read the document name here so the slow AI classification runs in the job scope (no ambient
        // transaction), not inside the command transaction. Reading it at processing time also avoids
        // adding Name to the payload — no backward-compat concern for in-flight queued jobs.
        var documentName = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == payload.DocumentId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

        var output = await classifier.ClassifyAsync(
            payload.DocumentId, documentName, payload.Scope, ct);

        // Dispatch the thinned command carrying the precomputed result — the handler only persists.
        var result = await sender.Send(new ApplyClassificationCommand
        {
            DocumentId = payload.DocumentId,
            Classification = output?.Classification,
            Scope = payload.Scope,
            ActorUserId = job.CreatedByUserId
        }, ct);

        if (job.ParentJobId is null)
        {
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                payload.DocumentId,
                result.DocumentName,
                error: null,
                job.CreatedByUserId,
                ct);
        }
    }
}
