using System.Globalization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommodityPriceSnapshot : IAuditSnapshot<CommodityPriceSnapshot, CommodityPrice>
{
    public required Guid CommodityId { get; init; }
    public required Guid QuoteCurrencyId { get; init; }
    public required DateTimeOffset PriceAt { get; init; }
    public required decimal Rate { get; init; }
    public required CommodityPriceSource Source { get; init; }
    public required Guid? CreatedByTransactionId { get; init; }

    public static CommodityPriceSnapshot From(CommodityPrice source) => new()
    {
        CommodityId = source.CommodityId,
        QuoteCurrencyId = source.QuoteCurrencyId,
        PriceAt = source.PriceAt,
        Rate = source.Rate,
        Source = source.Source,
        CreatedByTransactionId = source.CreatedByTransactionId,
    };

    public List<FieldChange> Diff(CommodityPriceSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (CommodityId != other.CommodityId)
            changes.Add(new ScalarFieldChange("CommodityId", CommodityId.ToString(), other.CommodityId.ToString()));
        if (QuoteCurrencyId != other.QuoteCurrencyId)
            changes.Add(new ScalarFieldChange("QuoteCurrencyId", QuoteCurrencyId.ToString(), other.QuoteCurrencyId.ToString()));
        if (PriceAt != other.PriceAt)
            changes.Add(new ScalarFieldChange("PriceAt",
                PriceAt.ToString("O", CultureInfo.InvariantCulture),
                other.PriceAt.ToString("O", CultureInfo.InvariantCulture)));
        if (Rate != other.Rate)
            changes.Add(new ScalarFieldChange("Rate",
                Rate.ToString(CultureInfo.InvariantCulture),
                other.Rate.ToString(CultureInfo.InvariantCulture)));
        if (Source != other.Source)
            changes.Add(new ScalarFieldChange("Source", Source.ToString(), other.Source.ToString()));
        if (CreatedByTransactionId != other.CreatedByTransactionId)
            changes.Add(new ScalarFieldChange("CreatedByTransactionId",
                CreatedByTransactionId?.ToString(),
                other.CreatedByTransactionId?.ToString()));

        return changes;
    }
}
