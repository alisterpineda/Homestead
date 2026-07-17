using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;

public class AddTransactionDocumentLinkHandler(
    AppDbContext db,
    IDocumentQueryService documentQuery,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AddTransactionDocumentLinkCommand, TransactionDocumentLinkResponse>
{
    public async Task<TransactionDocumentLinkResponse> Handle(
        AddTransactionDocumentLinkCommand request, CancellationToken ct)
    {
        var exists = await db.AccountTransactions
            .AsNoTracking()
            .AnyAsync(t => t.Id == request.TransactionId, ct);
        if (!exists)
            throw new EntityNotFoundException("Transaction not found.");

        // Accessible and not trashed — mirrors the statement-import initiate guard. 404 (not 403) so
        // an inaccessible document cannot be distinguished from a nonexistent one. The DeletedAt
        // predicate is redundant with Document's global query filter but kept explicit for parity.
        var docName = await documentQuery
            .FilterByAccess(db.Documents.AsNoTracking())
            .Where(d => d.Id == request.DocumentId && d.DeletedAt == null)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Document not found.");

        var duplicate = await db.TransactionDocumentLinks
            .AsNoTracking()
            .AnyAsync(l => l.AccountTransactionId == request.TransactionId
                        && l.DocumentId == request.DocumentId, ct);
        if (duplicate)
            throw new DuplicateEntityException("This document is already linked to this transaction.");

        var link = new TransactionDocumentLink
        {
            AccountTransactionId = request.TransactionId,
            DocumentId = request.DocumentId,
            Origin = TransactionDocumentLinkOrigin.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
        };
        db.TransactionDocumentLinks.Add(link);
        audit.Created("TransactionDocumentLink",
            TransactionDocumentLink.SyntheticId(request.TransactionId, request.DocumentId),
            parentEntityId: request.TransactionId);
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
