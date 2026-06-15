using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;
using Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;
using Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;
using Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;
using Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;
using Homestead.WebApi.Modules.Inventory.Features.Durables.DeleteDurable;
using Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurable;
using Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurableActivity;
using Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurables;
using Homestead.WebApi.Modules.Inventory.Features.Durables.PatchDurable;
using Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;
using Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;
using Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.AdjustStock;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.ConsumeStock;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.DeleteConsumable;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumableMovements;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.PatchConsumable;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.ReceiveStock;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.TransferStock;

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

        GetInventoryOverviewEndpoint.Map(v1);

        var items = v1.MapGroup("/items");
        GetItemEndpoint.Map(items);

        var places = v1.MapGroup("/places");
        GetPlaceContentsEndpoint.Map(places);

        var areas = v1.MapGroup("/areas");
        GetAreasEndpoint.Map(areas);
        CreateAreaEndpoint.Map(areas);
        PatchAreaEndpoint.Map(areas);
        DeleteAreaEndpoint.Map(areas);

        var durables = v1.MapGroup("/durables");
        GetDurablesEndpoint.Map(durables);
        GetDurableEndpoint.Map(durables);
        GetDurableActivityEndpoint.Map(durables);
        CreateDurableEndpoint.Map(durables);
        PatchDurableEndpoint.Map(durables);
        DeleteDurableEndpoint.Map(durables);

        var consumables = v1.MapGroup("/consumables");
        GetConsumablesEndpoint.Map(consumables);
        GetConsumableEndpoint.Map(consumables);
        GetConsumableMovementsEndpoint.Map(consumables);
        CreateConsumableEndpoint.Map(consumables);
        PatchConsumableEndpoint.Map(consumables);
        DeleteConsumableEndpoint.Map(consumables);
        ReceiveStockEndpoint.Map(consumables);
        ConsumeStockEndpoint.Map(consumables);
        TransferStockEndpoint.Map(consumables);
        AdjustStockEndpoint.Map(consumables);
        TrackDurableEndpoint.Map(consumables);
        CollapseDurablesEndpoint.Map(consumables);

        return app;
    }
}
