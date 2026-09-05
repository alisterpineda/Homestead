using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties;

/// <summary>One warranty row as the warranty endpoints return it.</summary>
public record ItemWarrantyResponse
{
    public required Guid Id { get; init; }
    public required Guid ItemId { get; init; }

    /// <summary>Who backs it; <c>null</c> means the manufacturer (the client renders the word).</summary>
    public string? Provider { get; init; }

    /// <summary><c>null</c> means no expiry (open-ended / lifetime).</summary>
    public DateOnly? ExpiresOn { get; init; }

    /// <summary>Single-line qualifier, e.g. "motor only".</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// This row's own cover as of the server's UTC date — never <see cref="WarrantyStatus.None"/>,
    /// which only an item with no rows at all can be. Derived, never stored.
    /// </summary>
    public required WarrantyStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
