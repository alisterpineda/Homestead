using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.GetTransactionDocuments;

public class GetTransactionDocumentsHandler(AppDbContext db, IDocumentQueryService documentQuery)
    : IRequestHandler<GetTransactionDocumentsQuery, IReadOnlyList<TransactionDocumentLinkResponse>>
{
    public async Task<IReadOnlyList<TransactionDocumentLinkResponse>> Handle(
        GetTransactionDocumentsQuery request, CancellationToken ct)
    {
        var links = await db.TransactionDocumentLinks
            .AsNoTracking()
            .Where(l => l.AccountTransactionId == request.TransactionId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        // A non-empty result proves the transaction exists (real FK on AccountTransactionId), so
        // the existence probe is only needed to tell 404 from a legitimately empty list.
        if (links.Count == 0)
        {
            var exists = await db.AccountTransactions
                .AsNoTracking()
                .AnyAsync(t => t.Id == request.TransactionId, ct);
            if (!exists)
                throw new EntityNotFoundException("Transaction not found.");
            return [];
        }

        // IncludeTrashed is what lets a trashed document be told apart from an inaccessible one
        // (under ActiveOnly, trashed docs would vanish and render as Unavailable).
        var docIds = links.Select(l => l.DocumentId).ToList();
        var docs = await documentQuery
            .AccessibleDocuments(TrashFilter.IncludeTrashed)
            .AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name, d.DeletedAt })
            .ToDictionaryAsync(d => d.Id, ct);

        return links.Select(l =>
        {
            // A dictionary miss means the document is outside the caller's access (a purged
            // document cannot leave a link behind — the FK cascades it away).
            var (name, availability) = docs.TryGetValue(l.DocumentId, out var doc)
                ? (doc.Name, doc.DeletedAt != null
                    ? DocumentLinkAvailability.Trashed
                    : DocumentLinkAvailability.Available)
                : ((string?)null, DocumentLinkAvailability.Unavailable);

            return new TransactionDocumentLinkResponse
            {
                DocumentId = l.DocumentId,
                DocumentName = name,
                Origin = l.Origin,
                Availability = availability,
                CreatedAt = l.CreatedAt,
            };
        }).ToList();
    }
}
