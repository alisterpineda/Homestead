using System.Globalization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AccountTransactionSplitSnapshot
    : IAuditSnapshot<AccountTransactionSplitSnapshot, AccountTransactionSplit>
{
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? UnitPriceRate { get; init; }
    public Guid? UnitPriceCommodityId { get; init; }
    public string? Memo { get; init; }

    public static AccountTransactionSplitSnapshot From(AccountTransactionSplit source) => new()
    {
        AccountId = source.AccountId,
        CommodityId = source.CommodityId,
        Quantity = source.Quantity,
        UnitPriceRate = source.UnitPriceRate,
        UnitPriceCommodityId = source.UnitPriceCommodityId,
        Memo = source.Memo,
    };

    public List<FieldChange> Diff(AccountTransactionSplitSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (AccountId != other.AccountId)
            changes.Add(new ScalarFieldChange("AccountId", AccountId.ToString(), other.AccountId.ToString()));
        if (CommodityId != other.CommodityId)
            changes.Add(new ScalarFieldChange("CommodityId", CommodityId.ToString(), other.CommodityId.ToString()));
        if (Quantity != other.Quantity)
            changes.Add(new ScalarFieldChange("Quantity",
                Quantity.ToString(CultureInfo.InvariantCulture),
                other.Quantity.ToString(CultureInfo.InvariantCulture)));
        if (UnitPriceRate != other.UnitPriceRate)
            changes.Add(new ScalarFieldChange("UnitPriceRate",
                UnitPriceRate?.ToString(CultureInfo.InvariantCulture),
                other.UnitPriceRate?.ToString(CultureInfo.InvariantCulture)));
        if (UnitPriceCommodityId != other.UnitPriceCommodityId)
            changes.Add(new ScalarFieldChange("UnitPriceCommodityId",
                UnitPriceCommodityId?.ToString(),
                other.UnitPriceCommodityId?.ToString()));
        if (Memo != other.Memo)
            changes.Add(new ScalarFieldChange("Memo", Memo, other.Memo));

        return changes;
    }
}
