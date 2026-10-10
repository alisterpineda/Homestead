using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.ReceiveStock;

public class ReceiveStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
{
    public async Task<ConsumableDetailResponse> Handle(ReceiveStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.ReceiveAsync(request.ConsumableId, new StockReceiveRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.Quantity, body.UnitCost, body.UnitCostCurrencyId, body.OccurredAt, body.Note,
            body.Source), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, request.ConsumableId, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
