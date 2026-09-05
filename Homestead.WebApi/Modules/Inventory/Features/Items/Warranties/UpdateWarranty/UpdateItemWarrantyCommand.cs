using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.UpdateWarranty;

/// <summary>Full replace of a warranty's three user-editable fields (PUT semantics, not a patch).</summary>
public record UpdateItemWarrantyCommand : ICommand<ItemWarrantyResponse>
{
    internal Guid ItemId { get; init; }
    internal Guid WarrantyId { get; init; }

    /// <summary>Who backs it (≤200); null/blank means the manufacturer.</summary>
    public string? Provider { get; init; }

    /// <summary>When cover lapses; null means no expiry.</summary>
    public DateOnly? ExpiresOn { get; init; }

    /// <summary>Single-line qualifier (≤500).</summary>
    public string? Notes { get; init; }
}
