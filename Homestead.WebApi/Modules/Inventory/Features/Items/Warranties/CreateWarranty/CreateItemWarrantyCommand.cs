using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.CreateWarranty;

/// <summary>All three fields are optional — an empty body creates a lifetime manufacturer warranty.</summary>
public record CreateItemWarrantyCommand : ICommand<ItemWarrantyResponse>
{
    /// <summary>The owning item, taken from the route by the endpoint (never from the body).</summary>
    internal Guid ItemId { get; init; }

    /// <summary>Who backs it (≤200); null/blank means the manufacturer.</summary>
    public string? Provider { get; init; }

    /// <summary>When cover lapses; null means no expiry. Past dates are legitimate (back-filling).</summary>
    public DateOnly? ExpiresOn { get; init; }

    /// <summary>Single-line qualifier (≤500).</summary>
    public string? Notes { get; init; }
}
