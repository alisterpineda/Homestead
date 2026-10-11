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
    public async Task<ConsumableDetailResponse> HandleAsync(Guid id, ReceiveStockRequest body, CancellationToken ct)
    {
        await stock.ReceiveAsync(id, new StockReceiveRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.Quantity, body.UnitCost, body.UnitCostCurrencyId, body.OccurredAt, body.Note,
            body.Source), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, id, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
