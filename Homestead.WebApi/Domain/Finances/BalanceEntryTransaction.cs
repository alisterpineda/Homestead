namespace Homestead.WebApi.Domain.Finances;

public class BalanceEntryTransaction : AccountTransaction
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid AssertedCommodityId { get; set; }
    public Commodity AssertedCommodity { get; set; } = null!;

    public decimal AssertedQuantity { get; set; }
}
