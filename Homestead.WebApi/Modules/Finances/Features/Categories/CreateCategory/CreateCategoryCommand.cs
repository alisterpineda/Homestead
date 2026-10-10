using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Categories;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.CreateCategory;

public record CreateCategoryCommand
{
    public required string Name { get; init; }
    public required AccountType Type { get; init; }

    // null → a top-level (root) category. Otherwise the parent category (same type, non-system).
    public Guid? ParentId { get; init; }
}
