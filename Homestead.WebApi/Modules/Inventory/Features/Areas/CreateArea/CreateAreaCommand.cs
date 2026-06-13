using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public record CreateAreaCommand : ICommand<AreaResponse>
{
    public required string Name { get; init; }

    /// <summary>Optional parent area for nesting; null (omitted) creates a top-level area.</summary>
    public Guid? ParentAreaId { get; init; }
}
