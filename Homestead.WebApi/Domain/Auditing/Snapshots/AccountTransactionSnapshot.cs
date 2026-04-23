using System.Globalization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AccountTransactionSnapshot : IAuditSnapshot<AccountTransactionSnapshot, AccountTransaction>
{
    public required DateOnly TxnDate { get; init; }
    public required string Description { get; init; }
    public required Guid CurrencyId { get; init; }
    public required TransactionType Type { get; init; }
    public required List<string> Splits { get; init; }

    public static AccountTransactionSnapshot From(AccountTransaction source) => new()
    {
        TxnDate = source.TxnDate,
        Description = source.Description,
        CurrencyId = source.CurrencyId,
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

        if (TxnDate != other.TxnDate)
            changes.Add(new ScalarFieldChange("TxnDate",
                TxnDate.ToString("O", CultureInfo.InvariantCulture),
                other.TxnDate.ToString("O", CultureInfo.InvariantCulture)));
        if (Description != other.Description)
            changes.Add(new ScalarFieldChange("Description", Description, other.Description));
        if (CurrencyId != other.CurrencyId)
            changes.Add(new ScalarFieldChange("CurrencyId",
                CurrencyId.ToString(), other.CurrencyId.ToString()));
        if (Type != other.Type)
            changes.Add(new ScalarFieldChange("Type", Type.ToString(), other.Type.ToString()));
        if (!Splits.SequenceEqual(other.Splits))
            changes.Add(new ArrayFieldChange("Splits", Splits, other.Splits));

        return changes;
    }

    private static string FormatSplit(AccountTransactionSplit s) =>
        string.Create(CultureInfo.InvariantCulture,
            $"account={s.AccountId}|commodity={s.CommodityId}|qty={s.Quantity}|val={s.Value}");
}
