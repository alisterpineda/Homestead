using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

/// <summary>Seed helpers shared by the item-note / item-activity handler suites.</summary>
internal static class ItemNoteTestData
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Item> AddItemAsync(
        TestAppDbContext db, Fungibility fungibility = Fungibility.NonFungible,
        ItemStatus status = ItemStatus.Active, string name = "Cordless drill")
    {
        var now = DateTimeOffset.UtcNow.AddDays(-30);
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = fungibility,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(Ct);
        return item;
    }

    public static async Task<AppUser> AddUserAsync(TestAppDbContext db, string displayName, string email)
    {
        var user = new AppUser { Id = Guid.NewGuid(), DisplayName = displayName, Email = email };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        return user;
    }

    public static async Task<ItemNote> AddNoteAsync(
        TestAppDbContext db, Guid itemId, DateTimeOffset occurredAt,
        string? subject = null, string body = "note body",
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null,
        Guid? createdByUserId = null, Guid? updatedByUserId = null, Guid? id = null)
    {
        var created = createdAt ?? occurredAt;
        var note = new ItemNote
        {
            Id = id ?? Guid.NewGuid(),
            ItemId = itemId,
            OccurredAt = occurredAt,
            Subject = subject,
            Body = body,
            CreatedAt = created,
            UpdatedAt = updatedAt ?? created,
            CreatedByUserId = createdByUserId,
            UpdatedByUserId = updatedByUserId ?? createdByUserId,
        };
        db.ItemNotes.Add(note);
        await db.SaveChangesAsync(Ct);
        return note;
    }
}
