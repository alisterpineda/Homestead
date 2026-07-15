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
    IJobQueue jobQueue)
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
        var version = await db.DocumentVersions
            .AsNoTracking()
            .Where(v => v.Id == request.DocumentVersionId)
            .Select(v => new { v.Id, v.DocumentId })
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Document version not found.");

        var documentAccessible = await documentQuery
            .FilterByAccess(db.Documents.AsNoTracking())
            .AnyAsync(d => d.Id == version.DocumentId && d.DeletedAt == null, ct);
        if (!documentAccessible)
            throw new EntityNotFoundException("Document not found.");

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
