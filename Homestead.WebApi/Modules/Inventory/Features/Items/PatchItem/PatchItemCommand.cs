using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.PatchItem;

/// <summary>
/// Patchable fields of a non-fungible item. Moving and unplacing are expressed as
/// <c>replace('/areaId', guid-or-null)</c> or <c>replace('/parentItemId', guid-or-null)</c> — a
/// cross-kind move naturally emits the op pair, and the server enforces at-most-one location.
/// There is no dedicated move endpoint in v1.
/// </summary>
public class PatchItemModel
{
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? Serial { get; set; }
    public DateOnly? AcquiredOn { get; set; }
    public decimal? Value { get; set; }
    public Guid? ValueCurrencyId { get; set; }
    public ItemStatus Status { get; set; }
    public string? LentTo { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? ParentItemId { get; set; }
    public bool IsContainer { get; set; }
    public string? Compartment { get; set; }
}

public record PatchItemCommand : ICommand<ItemResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchItemModel> PatchDocument { get; init; }
}
