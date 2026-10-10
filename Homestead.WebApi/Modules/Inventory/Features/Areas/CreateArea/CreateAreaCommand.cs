namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public record CreateAreaCommand
{
    public required string Name { get; init; }

    /// <summary>Optional parent area for nesting; null (omitted) creates a top-level area.</summary>
    public Guid? ParentAreaId { get; init; }
}
