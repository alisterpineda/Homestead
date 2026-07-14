using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.DeleteCategory;

public record DeleteCategoryCommand(Guid Id) : ICommand<bool>;
