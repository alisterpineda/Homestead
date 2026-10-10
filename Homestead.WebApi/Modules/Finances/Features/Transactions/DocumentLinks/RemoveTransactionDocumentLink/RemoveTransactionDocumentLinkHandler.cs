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
    public async Task Handle(RemoveTransactionDocumentLinkCommand request, CancellationToken ct)
    {
        var link = await db.TransactionDocumentLinks
            .FirstOrDefaultAsync(l => l.AccountTransactionId == request.TransactionId
                                   && l.DocumentId == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Document link not found.");

        // Import-origin links are the rollback scope — they die only with their transactions.
        if (link.Origin == TransactionDocumentLinkOrigin.StatementImport)
            throw new BusinessRuleException(
                "This link was created by a statement import. Roll back the import to remove it.");

        // Deliberately no document-access check: removing a link to a document the caller can no
        // longer access must work (the Unavailable-placeholder case), and removal reveals nothing.
        db.TransactionDocumentLinks.Remove(link);
        audit.Deleted("TransactionDocumentLink",
            TransactionDocumentLink.SyntheticId(request.TransactionId, request.DocumentId),
            parentEntityId: request.TransactionId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
