using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;

public class RemoveTransactionDocumentLinkHandler(
    AppDbContext db,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid transactionId, Guid documentId, CancellationToken ct)
    {
        var link = await db.TransactionDocumentLinks
            .FirstOrDefaultAsync(l => l.AccountTransactionId == transactionId
                                   && l.DocumentId == documentId, ct)
            ?? throw new EntityNotFoundException("Document link not found.");

        // Import-origin links are the rollback scope — they die only with their transactions.
        if (link.Origin == TransactionDocumentLinkOrigin.StatementImport)
            throw new BusinessRuleException(
                "This link was created by a statement import. Roll back the import to remove it.");

        // Deliberately no document-access check: removing a link to a document the caller can no
        // longer access must work (the Unavailable-placeholder case), and removal reveals nothing.
        db.TransactionDocumentLinks.Remove(link);
        audit.Deleted("TransactionDocumentLink",
            TransactionDocumentLink.SyntheticId(transactionId, documentId),
            parentEntityId: transactionId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
