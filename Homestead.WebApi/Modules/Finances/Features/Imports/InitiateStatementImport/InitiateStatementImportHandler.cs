using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.InitiateStatementImport;

public class InitiateStatementImportHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IDocumentQueryService documentQuery,
    IJobQueue jobQueue,
    ILogger<InitiateStatementImportHandler> logger)
    : IRequestHandler<InitiateStatementImportCommand, InitiateStatementImportResponse>
{
    public async Task<InitiateStatementImportResponse> Handle(
        InitiateStatementImportCommand request, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([request.AccountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");

        // Writable, user-facing balance-sheet account (rejects system/archived/category).
        AccountWritability.EnsureUserWritable(account);

        // Phase 1 reconciles a single closing balance. Investment accounts need holdings→balance-entry
        // matching (symbol→Commodity), which is deferred to a later phase.
        if (account.Kind == AccountKind.Investment)
            throw new BusinessRuleException(
                "Statement import is not yet supported for investment accounts.");

        // The document version must exist and its document must be accessible (and not trashed) to the user.
        // HasText rides this projection rather than a second query: command reads are separate autocommit
        // statements (the transaction opens lazily at the first write), so a follow-up read of the same row
        // could disagree with this one under a concurrent delete and 400 where this one would 404. The
        // IsNullOrWhiteSpace translates to SQL (btrim) — the column itself is never fetched.
        var version = await db.DocumentVersions
            .AsNoTracking()
            .Where(v => v.Id == request.DocumentVersionId)
            .Select(v => new { v.Id, v.DocumentId, HasText = !string.IsNullOrWhiteSpace(v.ExtractedText) })
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Document version not found.");

        var documentAccessible = await documentQuery
            .FilterByAccess(db.Documents.AsNoTracking())
            .AnyAsync(d => d.Id == version.DocumentId && d.DeletedAt == null, ct);
        if (!documentAccessible)
            throw new EntityNotFoundException("Document not found.");

        // Duplicate-import pre-check (the commit guard is authoritative; this one saves a metered AI parse):
        // one document = one active import, so any import-origin link to this document rejects re-initiate.
        // Origin-scoped — a manual link to the statement never blocks importing it. Placed after the access
        // guard so an inaccessible document cannot be probed for its import state.
        var alreadyImported = await db.TransactionDocumentLinks
            .AsNoTracking()
            .AnyAsync(TransactionDocumentLink.IsImportOriginFor(version.DocumentId), ct);
        if (alreadyImported)
            throw new BusinessRuleException(
                "This statement has already been imported. Roll back the previous import to import it again.");

        // Parsing is text-driven, so a version with no extracted text has nothing to import. Reject here —
        // before a job row exists and before any metered AI call — rather than letting the job stage a
        // proposal built from the page-1 image alone, which looks complete and silently drops later pages.
        // Thrown only after the access guard, so an inaccessible document cannot be probed for its text state.
        if (!version.HasText)
        {
            // Nothing else records this rejection: the exception handler doesn't log 400s, and by design no
            // job row exists to inspect afterwards. Without this line a wrong rejection would be invisible.
            logger.LogInformation(
                "Statement import rejected: version {VersionId} (document {DocumentId}) has no extracted text.",
                request.DocumentVersionId, version.DocumentId);

            // Deliberately conditional about reprocessing: an unsupported content type (CSV, spreadsheet)
            // extracts to null without ever throwing, so its reprocess job completes green and the advice
            // would loop forever.
            throw new BusinessRuleException(
                "This document has no extracted text, so there is nothing to import. If it is a PDF or a scan, "
                + "reprocess it in Documents and try again — CSV and spreadsheet statements are not supported.");
        }

        var jobId = await jobQueue.EnqueueAsync(
            ProcessingJobType.ParseAccountStatement,
            cfg =>
            {
                cfg.Payload = JsonSerializer.Serialize(
                    new ParseAccountStatementPayload(account.Id, version.Id));
                cfg.EntityType = ProcessingEntityType.Document;
                cfg.EntityId = version.DocumentId;
                cfg.CreatedByUserId = currentUser.UserId;
            },
            ct);

        return new InitiateStatementImportResponse { JobId = jobId };
    }
}
