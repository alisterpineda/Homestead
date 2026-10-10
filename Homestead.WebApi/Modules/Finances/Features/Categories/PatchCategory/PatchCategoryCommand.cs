namespace Homestead.WebApi.Modules.Finances.Features.Categories.PatchCategory;

// A dedicated command body (not JSON-patch): rename + reparent together, so cycle/same-type checks
// run server-side against the full intended state. Type is immutable — a category never changes direction.
public record PatchCategoryCommand
{
    public Guid Id { get; init; }
    public required string Name { get; init; }

    // The new parent, or null to move to root.
    public Guid? ParentId { get; init; }
}
