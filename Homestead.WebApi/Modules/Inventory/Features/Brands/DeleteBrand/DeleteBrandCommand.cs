using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.DeleteBrand;

public record DeleteBrandCommand(Guid Id) : ICommand<bool>;
