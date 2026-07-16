using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
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
    IAiProviderResolver aiProviderResolver,
    IStatementExtractionService extraction,
    StatementMappingBuilder mappingBuilder,
    AiPageRenderService aiPageRenderService,
    IProcessingNotifier notifier,
    IOptions<FileStorageOptions> storageOptions) : IJobHandler
{
    private readonly FileStorageOptions _storageOptions = storageOptions.Value;

    public ProcessingJobType Type => ProcessingJobType.ParseAccountStatement;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ParseAccountStatementPayload>()
            ?? throw new InvalidOperationException("ParseAccountStatement payload is missing or invalid.");

        // Hard AI dependency — no non-AI fallback. Surface a stable error code so the UI can explain why;
        // a defined-but-broken provider additionally carries its reason.
        var aiProvider = aiProviderResolver.Resolve()
            ?? throw new StatementParseException(
                "ai_unavailable",
                "AI is not configured; statement parsing is unavailable.");
        if (!aiProvider.IsConfigured)
            throw new StatementParseException(
                "ai_unavailable",
                $"AI provider '{aiProvider.Key}' is unavailable: {aiProvider.ConfigurationError}");

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

        // Extraction is Documents' job — this handler consumes ExtractedText, it never produces it.
        // InitiateStatementImportHandler rejects a text-less version at enqueue, so this is a backstop —
        // but not dead code; two paths reach it. (1) A reprocess landing between enqueue and run can empty
        // the text: the engine returning nothing is reported as *success* (TextExtractionHelper), and
        // ReprocessVersionTextCommand assigns that result unconditionally — while a *failed* reprocess
        // throws before persisting, preserving the old text. (2) The gate's IsNullOrWhiteSpace translates
        // to btrim over " \t\n\r" only, so text made of other whitespace (NBSP, \f, \v — plausible OCR
        // output for a blank scan) passes the gate but fails this C# check, with no race involved.
        var text = version.ExtractedText;
        if (string.IsNullOrWhiteSpace(text))
            throw new StatementParseException(
                "text_unavailable",
                $"Document version {version.Id} has no extracted text, so there is nothing to import. "
                + "If it is a PDF or a scan, reprocess it in Documents and try again.");

        // Optional vision supplement for scanned statements — same multimodal shape classification uses.
        // Cached by FileHash + edge, so an upload-warmed render is reused. Supplements the text above;
        // never substitutes for it — the render is page 1 only, so alone it would silently drop later pages.
        byte[]? pageImage = null;
        if (aiProvider.ImageInput)
        {
            var physicalPath = version.GetPhysicalPath(_storageOptions.MediaPath);
            pageImage = await aiPageRenderService.GetOrCreateAsync(
                version.FileHash, physicalPath, version.ContentType, aiProvider.ImageRenderMaxEdge, ct);
        }

        var ir = await extraction.ExtractAsync(text, pageImage, ct);
        var proposal = await mappingBuilder.BuildAsync(ir, account, ct);

        // Completeness safety-net: if the raw text overflowed the extraction budget it was clipped before the
        // model saw it (ClampText), so trailing lines may be missing. Surfaced to the reviewer as a warning.
        var textTruncated = text.Length > aiProvider.StatementMaxInputChars;

        var staged = new StagedStatementImport
        {
            AccountId = payload.AccountId,
            DocumentId = documentId,
            DocumentVersionId = version.Id,
            Ir = ir,
            Proposal = proposal,
            TextTruncated = textTruncated,
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
