using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

/// <summary>Seed helpers shared by the item-warranty handler suites (item/user seeding lives in ItemNoteTestData).</summary>
internal static class ItemWarrantyTestData
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<ItemWarranty> AddWarrantyAsync(
        TestAppDbContext db, Guid itemId,
        string? provider = null, DateOnly? expiresOn = null, string? notes = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null,
        Guid? createdByUserId = null, Guid? updatedByUserId = null, Guid? id = null)
    {
        var created = createdAt ?? DateTimeOffset.UtcNow.AddDays(-1);
        var warranty = new ItemWarranty
        {
            Id = id ?? Guid.NewGuid(),
            ItemId = itemId,
            Provider = provider,
            ExpiresOn = expiresOn,
            Notes = notes,
            CreatedAt = created,
            UpdatedAt = updatedAt ?? created,
            CreatedByUserId = createdByUserId,
            UpdatedByUserId = updatedByUserId ?? createdByUserId,
        };
        db.ItemWarranties.Add(warranty);
        await db.SaveChangesAsync(Ct);
        return warranty;
    }
}
