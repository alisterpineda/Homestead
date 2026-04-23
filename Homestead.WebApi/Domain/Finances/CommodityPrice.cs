using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class CommodityPrice
{
    public Guid Id { get; set; }

    public Guid CommodityId { get; set; }
    public Commodity Commodity { get; set; } = null!;

    public Guid QuoteCurrencyId { get; set; }
    public Commodity QuoteCurrency { get; set; } = null!;

    public DateOnly PriceDate { get; set; }
    public decimal Rate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
