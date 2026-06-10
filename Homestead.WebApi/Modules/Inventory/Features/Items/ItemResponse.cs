using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Items;

public record ItemResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Fungibility Fungibility { get; init; }
    public required ItemStatus Status { get; init; }
    public string? Notes { get; init; }

    public Guid? AreaId { get; init; }
    public string? AreaName { get; init; }
    public Guid? ParentItemId { get; init; }
    public required bool IsContainer { get; init; }
    public string? Compartment { get; init; }

    public decimal? Value { get; init; }
    public Guid? ValueCurrencyId { get; init; }
    public string? ValueCurrencySymbol { get; init; }

    public string? Serial { get; init; }
    public DateOnly? AcquiredOn { get; init; }
    public string? LentTo { get; init; }

    public string? Unit { get; init; }
    public decimal? RestockThreshold { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
