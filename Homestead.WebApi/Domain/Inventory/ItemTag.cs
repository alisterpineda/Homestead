namespace Homestead.WebApi.Domain.Inventory;

public class ItemTag
{
    public Guid ItemId { get; set; }
    public Guid TagId { get; set; }

    public Item Item { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
