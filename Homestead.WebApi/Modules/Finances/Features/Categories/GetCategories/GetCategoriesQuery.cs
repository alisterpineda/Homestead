using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.GetCategories;

// Non-paginated: the whole category tree (few dozen rows). Type filters to one direction; omit for both.
public record GetCategoriesQuery : IRequest<IReadOnlyList<CategoryResponse>>
{
    public AccountType? Type { get; init; }
}
