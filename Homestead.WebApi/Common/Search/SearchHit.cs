namespace Homestead.WebApi.Common.Search;

/// <summary>
/// A single universal-search result. Source-agnostic: <see cref="Id"/> is a string so any source's
/// key fits, and <see cref="Type"/> is the source-internal kind (e.g. "durable" / "consumable")
/// the frontend presenter uses to pick an icon and route.
/// </summary>
/// <param name="Source">Owning source key (matches <see cref="ISearchSource.Source"/>).</param>
/// <param name="Type">Source-internal kind, consumed by the frontend presenter for icon + routing.</param>
/// <param name="Id">Source-internal identifier (inventory uses <c>Item.Id.ToString()</c>).</param>
/// <param name="Title">Primary display text.</param>
/// <param name="Subtitle">Optional secondary line (e.g. a location or on-hand count).</param>
/// <param name="Badge">Optional short status pill (e.g. "Lent", "Low").</param>
public record SearchHit(
    string Source,
    string Type,
    string Id,
    string Title,
    string? Subtitle,
    string? Badge);
