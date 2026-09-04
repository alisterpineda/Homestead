using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes;

public static class ItemNoteResponseMapper
{
    // Optional-nav projection: EF emits LEFT JOINs to users, InMemory handles it, and a note whose
    // author has since been deleted (SetNull FK) still projects with null attribution.
    public static readonly Expression<Func<ItemNote, ItemNoteResponse>> Projection = n => new ItemNoteResponse
    {
        Id = n.Id,
        ItemId = n.ItemId,
        OccurredAt = n.OccurredAt,
        Body = n.Body,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
        IsEdited = n.UpdatedAt != n.CreatedAt,
        CreatedByDisplayName = n.CreatedByUser != null ? n.CreatedByUser.DisplayName : null,
        CreatedByEmail = n.CreatedByUser != null ? n.CreatedByUser.Email : null,
        UpdatedByDisplayName = n.UpdatedByUser != null ? n.UpdatedByUser.DisplayName : null,
        UpdatedByEmail = n.UpdatedByUser != null ? n.UpdatedByUser.Email : null,
    };
}
