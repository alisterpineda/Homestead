using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Finances.Processing;

/// <summary>
/// Parses an uploaded statement (a <see cref="DocumentVersion"/>) into a <see cref="StatementIr"/>, builds a
/// deterministic <see cref="StatementMappingProposal"/>, and stages the whole thing onto
/// <c>ProcessingJob.Result</c> for human review at <c>GET /finances/imports/{jobId}</c>. Nothing touches the
/// ledger here — that happens only on an explicit commit. Modelled on the slow-work-then-thin-persist shape
/// of <see cref="ImportCommodityPricesJobHandler"/> plus <c>ClassifyDocumentJobHandler</c>'s leaf notify.
/// </summary>
public class ParseAccountStatementJobHandler(
    AppDbContext db,
    IAiClientFactory aiClientFactory,
    IStatementExtractionService extraction,
    StatementMappingBuilder mappingBuilder,
    AiPageRenderService aiPageRenderService,
    TextExtractionHelper textExtraction,
    IProcessingNotifier notifier,
    IOptions<FileStorageOptions> storageOptions,
    IOptions<AiOptions> aiOptions,
    ILogger<ParseAccountStatementJobHandler> logger) : IJobHandler
{
    private readonly FileStorageOptions _storageOptions = storageOptions.Value;
    private readonly AiOptions _aiOptions = aiOptions.Value;

    public ProcessingJobType Type => ProcessingJobType.ParseAccountStatement;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ParseAccountStatementPayload>()
            ?? throw new InvalidOperationException("ParseAccountStatement payload is missing or invalid.");

        // Hard AI dependency — no non-AI fallback. Surface a stable error code so the UI can explain why.
        if (!aiClientFactory.IsConfigured)
            throw new StatementParseException(
                "ai_unavailable",
                "AI is not configured; statement parsing is unavailable.");

        var version = await db.DocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == payload.DocumentVersionId, ct)
            ?? throw new StatementParseException(
                "version_not_found",
                $"Document version {payload.DocumentVersionId} was not found.");

        var documentId = version.DocumentId;
        var documentName = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new StatementParseException(
                "document_not_found",
                $"Document {documentId} was not found.");

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == payload.AccountId, ct)
            ?? throw new StatementParseException(
                "account_not_found",
                $"Account {payload.AccountId} was not found.");

        var physicalPath = version.GetPhysicalPath(_storageOptions.MediaPath);

        // ExtractedText is normally populated at upload; extract defensively if it is missing.
        var text = version.ExtractedText;
        if (string.IsNullOrWhiteSpace(text))
        {
            var extractionResult = await textExtraction.ExtractTextSafeAsync(
                physicalPath, version.ContentType, ct);
            if (!extractionResult.Succeeded)
                logger.LogWarning(extractionResult.Error,
                    "Text extraction failed for statement version {VersionId}; extracting from image only.",
                    version.Id);
            text = extractionResult.Text ?? string.Empty;
        }

        // Optional vision supplement for scanned statements — same multimodal shape classification uses.
        // Cached by FileHash, so an upload-warmed render is reused.
        byte[]? pageImage = null;
        if (_aiOptions.ImageInput)
        {
            pageImage = await aiPageRenderService.GetOrCreateAsync(
                version.FileHash, physicalPath, version.ContentType, ct);
        }

        var ir = await extraction.ExtractAsync(text, pageImage, ct);
        var proposal = await mappingBuilder.BuildAsync(ir, account, ct);

        var staged = new StagedStatementImport
        {
            AccountId = payload.AccountId,
            DocumentId = documentId,
            DocumentVersionId = version.Id,
            Ir = ir,
            Proposal = proposal,
        };

        // Persist the staged result BEFORE notifying so the review GET (which reads from a separate scope)
        // can never race ahead of the write. The job instance is tracked by this scope's AppDbContext; the
        // processor's post-return status stamp is then a harmless idempotent save.
        job.SetResult(staged);
        job.EntityType = ProcessingEntityType.Document;
        job.EntityId = documentId;
        await db.SaveChangesAsync(ct);

        // Leaf-only notify (this job never has a parent), mirroring ClassifyDocumentJobHandler. Background
        // scope ⇒ direct call, no command transaction to defer to.
        if (job.ParentJobId is null)
        {
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                documentId,
                documentName,
                error: null,
                job.CreatedByUserId,
                ct);
        }
    }
}
