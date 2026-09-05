using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.DeleteWarranty;

public record DeleteItemWarrantyCommand(Guid ItemId, Guid WarrantyId) : ICommand<bool>;
