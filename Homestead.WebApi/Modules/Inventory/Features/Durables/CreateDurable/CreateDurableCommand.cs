using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;

/// <summary>
/// Creates a non-fungible item. The server hard-codes <c>Fungibility = NonFungible</c> and
/// <c>Status = Active</c>. Location is at most one of <see cref="AreaId"/> / <see cref="ParentItemId"/>;
/// <see cref="Compartment"/> is only meaningful inside a container.
/// </summary>
public record CreateDurableCommand : ICommand<DurableResponse>
{
    public required string Name { get; init; }
    public Guid? AreaId { get; init; }

    /// <summary>Place the new item inside this holder (container or assembly). At most one of this / <see cref="AreaId"/>.</summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>Holder-role of the new item; defaults to <see cref="DurableKind.Plain"/> (holds nothing).</summary>
    public DurableKind DurableKind { get; init; } = DurableKind.Plain;

    /// <summary>Free-text sub-position; only allowed when <see cref="ParentItemId"/> is set.</summary>
    public string? Compartment { get; init; }

    public string? Notes { get; init; }
    public string? Serial { get; init; }

    /// <summary>Optional canonical brand this item is attributed to.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>Optional free-text model designation ("MRW23").</summary>
    public string? Model { get; init; }

    public DateOnly? AcquiredOn { get; init; }
    public decimal? Value { get; init; }
    public Guid? ValueCurrencyId { get; init; }
}
