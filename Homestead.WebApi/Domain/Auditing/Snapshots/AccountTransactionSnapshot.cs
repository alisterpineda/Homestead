using System.Globalization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AccountTransactionSnapshot : IAuditSnapshot<AccountTransactionSnapshot, AccountTransaction>
{
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Description { get; init; }
    public required TransactionType Type { get; init; }
    public required List<string> Splits { get; init; }

    public static AccountTransactionSnapshot From(AccountTransaction source) => new()
    {
        OccurredAt = source.OccurredAt,
        Description = source.Description,
        Type = source.Type,
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
        if (Description != other.Description)
            changes.Add(new ScalarFieldChange("Description", Description, other.Description));
        if (Type != other.Type)
            changes.Add(new ScalarFieldChange("Type", Type.ToString(), other.Type.ToString()));
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
