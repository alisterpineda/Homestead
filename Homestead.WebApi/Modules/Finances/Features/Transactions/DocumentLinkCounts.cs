using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions;

internal static class DocumentLinkCounts
{
    /// <summary>
    /// Single-transaction link count as the aux map <see cref="TransactionMapping.ToResponse"/>
    /// expects — for handlers that return one freshly read or patched transaction.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, int>> ForAsync(
        AppDbContext db, Guid transactionId, CancellationToken ct)
    {
        var count = await db.TransactionDocumentLinks
            .AsNoTracking()
            .CountAsync(l => l.AccountTransactionId == transactionId, ct);
        return new Dictionary<Guid, int> { [transactionId] = count };
    }

    /// <summary>
    /// One grouped count over a page of transaction ids — the paginated readers' variant. Keeps
    /// the hot list queries inside the finances schema (no join into documents).
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, int>> ForPageAsync(
        AppDbContext db, IReadOnlyList<Guid> transactionIds, CancellationToken ct)
    {
        return await db.TransactionDocumentLinks
            .AsNoTracking()
            .Where(l => transactionIds.Contains(l.AccountTransactionId))
            .GroupBy(l => l.AccountTransactionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }
}
