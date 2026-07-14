using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.Categories;

/// <summary>
/// A category account in the categories tree. Deliberately NOT <c>AccountResponse</c> — categories carry
/// no valuation. <see cref="Type"/> is Income or Expense. <see cref="ReadOnly"/> mirrors
/// <see cref="IsSystem"/> and gates edit/delete/reparent (the system Uncategorised buckets are read-only).
/// </summary>
public record CategoryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AccountType Type { get; init; }
    public required Guid? ParentId { get; init; }
    public required int Depth { get; init; }
    public required string Path { get; init; }
    public required bool IsSystem { get; init; }
    public required bool ReadOnly { get; init; }
}

internal static class CategoryMapping
{
    public static CategoryResponse ToResponse(
        Guid id, string name, AccountType type, Guid? parentId, bool isSystem, CategoryPathInfo info) =>
        new()
        {
            Id = id,
            Name = name,
            Type = type,
            ParentId = parentId,
            Depth = info.Depth,
            Path = info.Path,
            IsSystem = isSystem,
            ReadOnly = isSystem,
        };
}
