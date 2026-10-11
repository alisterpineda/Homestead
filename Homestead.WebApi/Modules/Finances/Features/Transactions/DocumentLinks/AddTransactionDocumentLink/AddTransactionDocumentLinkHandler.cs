using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;

public class AddTransactionDocumentLinkHandler(
    AppDbContext db,
    IDocumentQueryService documentQuery,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
{
    public async Task<TransactionDocumentLinkResponse> HandleAsync(
        Guid transactionId, AddTransactionDocumentLinkRequest body, CancellationToken ct)
    {
        var exists = await db.AccountTransactions
            .AsNoTracking()
            .AnyAsync(t => t.Id == transactionId, ct);
        if (!exists)
            throw new EntityNotFoundException("Transaction not found.");

        // Accessible and not trashed (ActiveOnly default) — mirrors the statement-import initiate
        // guard. 404 (not 403) so an inaccessible document cannot be distinguished from a
        // nonexistent one.
        var docName = await documentQuery
            .AccessibleDocuments()
            .AsNoTracking()
            .Where(d => d.Id == body.DocumentId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Document not found.");

        var duplicate = await db.TransactionDocumentLinks
            .AsNoTracking()
            .AnyAsync(l => l.AccountTransactionId == transactionId
                        && l.DocumentId == body.DocumentId, ct);
        if (duplicate)
            throw new DuplicateEntityException("This document is already linked to this transaction.");

        var link = new TransactionDocumentLink
        {
            AccountTransactionId = transactionId,
            DocumentId = body.DocumentId,
            Origin = TransactionDocumentLinkOrigin.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
        };
        db.TransactionDocumentLinks.Add(link);
        audit.Created("TransactionDocumentLink",
            TransactionDocumentLink.SyntheticId(transactionId, body.DocumentId),
            parentEntityId: transactionId);
        await unitOfWork.SaveChangesAsync(ct);

        return new TransactionDocumentLinkResponse
        {
            DocumentId = link.DocumentId,
            DocumentName = docName,
            Origin = link.Origin,
            Availability = DocumentLinkAvailability.Available,
            CreatedAt = link.CreatedAt,
        };
    }
}
