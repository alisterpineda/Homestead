using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.PatchConsumable;

/// <summary>
/// Patchable metadata of a consumable. Quantity is never edited here — it changes only through the
/// stock operations (receive/consume/transfer/adjust). <see cref="Status"/> accepts only the reduced
/// fungible lifecycle (<c>Active</c> / <c>Archived</c>), enforced server-side.
/// </summary>
public class PatchConsumableModel
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal? RestockThreshold { get; set; }
    public Guid? BrandId { get; set; }
    public string? Notes { get; set; }
    public ItemStatus Status { get; set; }

    /// <summary>
    /// Whole desired classification tag set as slash-delimited paths. Seeded from the item's current
    /// tags, so a patch that omits <c>/tags</c> leaves them untouched; a <c>replace('/tags', […])</c>
    /// replaces the entire set (missing ancestors upserted).
    /// </summary>
    public List<string> Tags { get; set; } = [];
}

public record PatchConsumableCommand : ICommand<ConsumableResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchConsumableModel> PatchDocument { get; init; }
}
