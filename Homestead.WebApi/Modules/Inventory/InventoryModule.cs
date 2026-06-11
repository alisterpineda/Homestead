using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;
using Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;
using Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;
using Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;
using Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;
using Homestead.WebApi.Modules.Inventory.Features.Items.DeleteItem;
using Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;
using Homestead.WebApi.Modules.Inventory.Features.Items.GetItems;
using Homestead.WebApi.Modules.Inventory.Features.Items.PatchItem;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.AdjustStock;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.ConsumeStock;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.CreateSupply;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.DeleteSupply;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupplies;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupply;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupplyMovements;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.PatchSupply;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.ReceiveStock;
using Homestead.WebApi.Modules.Inventory.Features.Supplies.TransferStock;

namespace Homestead.WebApi.Modules.Inventory;

public static class InventoryModule
{
    public static IVersionedEndpointRouteBuilder MapInventoryEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/inventory")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Inventory")
            .RequireAuthorization(AuthPolicies.ActiveUser);

        var areas = v1.MapGroup("/areas");
        GetAreasEndpoint.Map(areas);
        CreateAreaEndpoint.Map(areas);
        PatchAreaEndpoint.Map(areas);
        DeleteAreaEndpoint.Map(areas);

        var items = v1.MapGroup("/items");
        GetItemsEndpoint.Map(items);
        GetItemEndpoint.Map(items);
        CreateItemEndpoint.Map(items);
        PatchItemEndpoint.Map(items);
        DeleteItemEndpoint.Map(items);

        var supplies = v1.MapGroup("/supplies");
        GetSuppliesEndpoint.Map(supplies);
        GetSupplyEndpoint.Map(supplies);
        GetSupplyMovementsEndpoint.Map(supplies);
        CreateSupplyEndpoint.Map(supplies);
        PatchSupplyEndpoint.Map(supplies);
        DeleteSupplyEndpoint.Map(supplies);
        ReceiveStockEndpoint.Map(supplies);
        ConsumeStockEndpoint.Map(supplies);
        TransferStockEndpoint.Map(supplies);
        AdjustStockEndpoint.Map(supplies);

        return app;
    }
}
