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
    IAiProvider aiProvider,
    AiInputSettings aiSettings,
    IStatementExtractionService extraction,
    StatementMappingBuilder mappingBuilder,
    IPayeeResolutionService payeeResolution,
    AiPageRenderService aiPageRenderService,
    IProcessingNotifier notifier,
    ILogger<ParseAccountStatementJobHandler> logger,
    IOptions<FileStorageOptions> storageOptions) : IJobHandler
{
    // Upper bound on the payee catalog sent to the AI resolver — index-referenced, so a household's full
    // payee list fits comfortably; the cap only bounds a pathological catalog.
    private const int PayeeCatalogCap = 500;

    private readonly FileStorageOptions _storageOptions = storageOptions.Value;

    public ProcessingJobType Type => ProcessingJobType.ParseAccountStatement;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ParseAccountStatementPayload>()
            ?? throw new InvalidOperationException("ParseAccountStatement payload is missing or invalid.");

        // Hard AI dependency — no non-AI fallback. Surface a stable error code so the UI can explain why;
        // present-but-broken config additionally carries its reason. (The E2E suite pins the absent-case text.)
        if (!aiProvider.Availability.IsPresent)
            throw new StatementParseException(
                "ai_unavailable",
                "AI is not configured; statement parsing is unavailable.");
        if (!aiProvider.Availability.IsConfigured)
            throw new StatementParseException(
                "ai_unavailable",
                $"AI is unavailable: {aiProvider.Availability.Reason}; statement parsing is unavailable.");

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
        if (aiSettings.ImageInput)
        {
            var physicalPath = version.GetPhysicalPath(_storageOptions.MediaPath);
            pageImage = await aiPageRenderService.GetOrCreateAsync(
                version.FileHash, physicalPath, version.ContentType, aiSettings.ImageRenderMaxEdge, ct);
        }

        var ir = await extraction.ExtractAsync(text, pageImage, ct);
        var proposal = await mappingBuilder.BuildAsync(ir, account, ct);

        // AI second pass (tiers ②/③): resolve payees for lines the deterministic learning table left at
        // SuggestionSource.None. A separate, small, non-streaming call — isolated from the streaming
        // extraction above. AI is a hard prerequisite for parse, so it's configured by construction here.
        proposal = await ApplyAiPayeeSuggestionsAsync(proposal, ct);

        // Completeness safety-net: if the raw text overflowed the extraction budget it was clipped before the
        // model saw it (ClampText), so trailing lines may be missing. Surfaced to the reviewer as a warning.
        var textTruncated = text.Length > aiSettings.StatementMaxInputChars;

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

    /// <summary>
    /// Merge AI tier-②/③ payee suggestions into the lines the learning table left at
    /// <see cref="PayeeSuggestionSource.None"/>. Best-effort: a resolution failure logs and returns the
    /// proposal untouched (tier-① learned suggestions survive), never failing the import.
    /// </summary>
    private async Task<StatementMappingProposal> ApplyAiPayeeSuggestionsAsync(
        StatementMappingProposal proposal, CancellationToken ct)
    {
        var pending = proposal.LineItems
            .Where(f => f.SuggestionSource == PayeeSuggestionSource.None
                        && !string.IsNullOrWhiteSpace(f.Description))
            .Select(f => new PayeeResolutionInput(f.Index, f.Description!))
            .ToList();

        if (pending.Count == 0)
            return proposal;

        var catalog = await db.Payees
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .OrderBy(p => p.Name)
            .Take(PayeeCatalogCap)
            .Select(p => new PayeeCatalogEntry(p.Id, p.Name))
            .ToListAsync(ct);

        IReadOnlyDictionary<int, PayeeResolution> resolutions;
        try
        {
            resolutions = await payeeResolution.ResolveAsync(pending, catalog, ct);
        }
        // Best-effort only: a genuine resolution failure keeps tier-① and never fails the import. A
        // cancellation is NOT a failure — let it propagate so a shutdown actually cancels the job.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI payee resolution failed; continuing with learned suggestions only.");
            return proposal;
        }

        if (resolutions.Count == 0)
            return proposal;

        var merged = proposal.LineItems
            .Select(f => resolutions.TryGetValue(f.Index, out var r) ? ApplyResolution(f, r) : f)
            .ToList();

        return proposal with { LineItems = merged };
    }

    // Existing-payee match → tier ②; a proposed new name → tier ③; nothing → the line is left as-is.
    private static ProposedFlow ApplyResolution(ProposedFlow flow, PayeeResolution resolution)
    {
        if (resolution.ExistingPayeeId is Guid existingId)
            return flow with
            {
                SuggestedPayeeId = existingId,
                SuggestedPayeeName = null,
                SuggestionSource = PayeeSuggestionSource.AiExistingPayee,
            };

        if (!string.IsNullOrWhiteSpace(resolution.NewPayeeName))
            return flow with
            {
                SuggestedPayeeId = null,
                SuggestedPayeeName = resolution.NewPayeeName,
                SuggestionSource = PayeeSuggestionSource.AiNewPayee,
            };

        return flow;
    }
}
