using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Owns the one-to-one lifecycle between annotated splits and <see cref="CommodityPrice"/> rows.
/// Every annotated split (non-null UnitPriceRate + UnitPriceCommodityId) materialises as one
/// linked price row at (Split.Commodity, Split.UnitPriceCommodityId, Transaction.OccurredAt).
///
/// Services never call SaveChangesAsync; the calling handler owns the unit of work.
/// </summary>
public interface ILinkedPriceManager
{
    /// <summary>
    /// After a transaction's splits are in their final state (create or edit), synchronise
    /// linked price rows: create rows for newly-annotated splits, update rows for changed
    /// annotations, delete rows whose split is no longer annotated. Rejects on
    /// (commodity, quote, at) collisions. Emits audit entries for every change.
    /// </summary>
    Task SyncForTransactionAsync(AccountTransaction transaction, CancellationToken ct);

    /// <summary>
    /// Records audit-log Deleted entries for every linked price row on this transaction.
    /// The actual DB row deletion happens via FK cascade when the transaction is removed,
    /// but the audit log would otherwise be silent — this makes the cascade explicit.
    /// </summary>
    Task AuditCascadeDeletesAsync(Guid transactionId, CancellationToken ct);
}
