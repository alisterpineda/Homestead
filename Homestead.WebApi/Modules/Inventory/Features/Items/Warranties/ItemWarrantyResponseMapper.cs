using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties;

public static class ItemWarrantyResponseMapper
{
    /// <summary>
    /// A plain method rather than an EF expression: the row status needs <paramref name="today"/>, and
    /// warranty reads are already small per-item lists materialised before mapping.
    /// </summary>
    public static ItemWarrantyResponse ToResponse(ItemWarranty warranty, DateOnly today) => new()
    {
        Id = warranty.Id,
        ItemId = warranty.ItemId,
        Provider = warranty.Provider,
        ExpiresOn = warranty.ExpiresOn,
        Notes = warranty.Notes,
        Status = WarrantyStatusRules.ForRow(warranty.ExpiresOn, today),
        CreatedAt = warranty.CreatedAt,
        UpdatedAt = warranty.UpdatedAt,
    };
}
