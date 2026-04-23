using System.Globalization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommodityPriceSnapshot : IAuditSnapshot<CommodityPriceSnapshot, CommodityPrice>
{
    public required Guid CommodityId { get; init; }
    public required Guid QuoteCurrencyId { get; init; }
    public required DateOnly PriceDate { get; init; }
    public required decimal Rate { get; init; }

    public static CommodityPriceSnapshot From(CommodityPrice source) => new()
    {
        CommodityId = source.CommodityId,
        QuoteCurrencyId = source.QuoteCurrencyId,
        PriceDate = source.PriceDate,
        Rate = source.Rate,
    };

    public List<FieldChange> Diff(CommodityPriceSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (CommodityId != other.CommodityId)
            changes.Add(new ScalarFieldChange("CommodityId", CommodityId.ToString(), other.CommodityId.ToString()));
        if (QuoteCurrencyId != other.QuoteCurrencyId)
            changes.Add(new ScalarFieldChange("QuoteCurrencyId", QuoteCurrencyId.ToString(), other.QuoteCurrencyId.ToString()));
        if (PriceDate != other.PriceDate)
            changes.Add(new ScalarFieldChange("PriceDate", PriceDate.ToString("O"), other.PriceDate.ToString("O")));
        if (Rate != other.Rate)
            changes.Add(new ScalarFieldChange("Rate",
                Rate.ToString(CultureInfo.InvariantCulture),
                other.Rate.ToString(CultureInfo.InvariantCulture)));

        return changes;
    }
}
