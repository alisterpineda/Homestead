using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions;

internal static class PayeeNames
{
    /// <summary>
    /// Payee-id → name map over a page of transactions — the aux map
    /// <see cref="TransactionMapping.ToResponse"/> expects. Collects the distinct non-null payee ids of the
    /// flows on the page and looks up their names in one query (empty-dict shortcut when the page has none).
    /// Keeps the hot list queries flat: no join into payees, one small batch lookup.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ForPageAsync(
        AppDbContext db, IReadOnlyList<AccountTransaction> transactions, CancellationToken ct)
    {
        var ids = transactions
            .OfType<NormalTransaction>()
            .Where(t => t.PayeeId is not null)
            .Select(t => t.PayeeId!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await db.Payees
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
    }

    /// <summary>
    /// Single-transaction payee map — for handlers that return one freshly read or written transaction.
    /// </summary>
    public static Task<IReadOnlyDictionary<Guid, string>> ForAsync(
        AppDbContext db, AccountTransaction transaction, CancellationToken ct)
        => ForPageAsync(db, [transaction], ct);
}
