using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class AccountTransactionSplit
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }
    public AccountTransaction Transaction { get; set; } = null!;

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid CommodityId { get; set; }
    public Commodity Commodity { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal? UnitPriceRate { get; set; }
    public Guid? UnitPriceCommodityId { get; set; }
    public Commodity? UnitPriceCommodity { get; set; }

    public string? Memo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
