using Homestead.WebApi.Common;

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

    /// <summary>Place the new item inside this container. At most one of this / <see cref="AreaId"/>.</summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>Whether the new item is itself a container.</summary>
    public bool IsContainer { get; init; }

    /// <summary>Free-text sub-position; only allowed when <see cref="ParentItemId"/> is set.</summary>
    public string? Compartment { get; init; }

    public string? Notes { get; init; }
    public string? Serial { get; init; }
    public DateOnly? AcquiredOn { get; init; }
    public decimal? Value { get; init; }
    public Guid? ValueCurrencyId { get; init; }
}
