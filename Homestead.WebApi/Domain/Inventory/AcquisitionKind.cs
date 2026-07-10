namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// How a durable entered the household. One behavioral value (<see cref="Purchased"/> — the only
/// kind where a purchase price is expected), two provenance values (<see cref="Gift"/>,
/// <see cref="Inherited"/>), and a catch-all (<see cref="Other"/>). Null means "not recorded" —
/// the normal state for a backfilled household.
/// </summary>
public enum AcquisitionKind
{
    Purchased,
    Gift,
    Inherited,
    Other,
}
