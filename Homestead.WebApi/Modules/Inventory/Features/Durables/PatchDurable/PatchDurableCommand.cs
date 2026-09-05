using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.PatchDurable;

/// <summary>
/// Patchable fields of a non-fungible item. Moving and unplacing are expressed as
/// <c>replace('/areaId', guid-or-null)</c> or <c>replace('/parentItemId', guid-or-null)</c> — a
/// cross-kind move naturally emits the op pair, and the server enforces at-most-one location.
/// There is no dedicated move endpoint in v1.
/// </summary>
public class PatchDurableModel
{
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? Serial { get; set; }
    public Guid? BrandId { get; set; }
    public string? Model { get; set; }
    public DateOnly? AcquiredOn { get; set; }
    public AcquisitionKind? AcquisitionKind { get; set; }
    public decimal? AcquisitionCost { get; set; }
    public Guid? AcquisitionCostCurrencyId { get; set; }
    public string? AcquiredFrom { get; set; }
    public decimal? Value { get; set; }
    public Guid? ValueCurrencyId { get; set; }
    public ItemStatus Status { get; set; }
    public string? StatusNote { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? ParentItemId { get; set; }
    public bool? IsContainer { get; set; }
    public string? Compartment { get; set; }

    /// <summary>
    /// Whole desired classification tag set as slash-delimited paths. Seeded from the item's current
    /// tags, so a patch that omits <c>/tags</c> leaves them untouched; a <c>replace('/tags', […])</c>
    /// replaces the entire set (missing ancestors upserted).
    /// </summary>
    public List<string> Tags { get; set; } = [];
}

public record PatchDurableCommand : ICommand<DurableResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchDurableModel> PatchDocument { get; init; }
}
