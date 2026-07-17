using System.Globalization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AccountTransactionSnapshot : IAuditSnapshot<AccountTransactionSnapshot, AccountTransaction>
{
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Memo { get; init; }
    public required TransactionType Type { get; init; }
    public Guid? PayeeId { get; init; }
    public required List<string> Splits { get; init; }

    public static AccountTransactionSnapshot From(AccountTransaction source) => new()
    {
        OccurredAt = source.OccurredAt,
        Memo = source.Memo,
        Type = source.Type,
        PayeeId = (source as NormalTransaction)?.PayeeId,
        Splits = source.Splits
            .OrderBy(s => s.AccountId)
            .ThenBy(s => s.CommodityId)
            .Select(FormatSplit)
            .ToList(),
    };

    public List<FieldChange> Diff(AccountTransactionSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (OccurredAt != other.OccurredAt)
            changes.Add(new ScalarFieldChange("OccurredAt",
                OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                other.OccurredAt.ToString("O", CultureInfo.InvariantCulture)));
        if (Memo != other.Memo)
            changes.Add(new ScalarFieldChange("Memo", Memo, other.Memo));
        if (Type != other.Type)
            changes.Add(new ScalarFieldChange("Type", Type.ToString(), other.Type.ToString()));
        if (PayeeId != other.PayeeId)
            changes.Add(new ScalarFieldChange("PayeeId", PayeeId?.ToString(), other.PayeeId?.ToString()));
        if (!Splits.SequenceEqual(other.Splits))
            changes.Add(new ArrayFieldChange("Splits", Splits, other.Splits));

        return changes;
    }

    private static string FormatSplit(AccountTransactionSplit s)
    {
        var rate = s.UnitPriceRate?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var quote = s.UnitPriceCommodityId?.ToString() ?? "-";
        return string.Create(CultureInfo.InvariantCulture,
            $"account={s.AccountId}|commodity={s.CommodityId}|qty={s.Quantity}|unitPriceRate={rate}|unitPriceCommodity={quote}");
    }
}
