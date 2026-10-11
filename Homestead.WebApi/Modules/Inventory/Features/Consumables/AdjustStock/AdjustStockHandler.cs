using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.AdjustStock;

public class AdjustStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
{
    public async Task<ConsumableDetailResponse> HandleAsync(Guid id, AdjustStockRequest body, CancellationToken ct)
    {
        await stock.AdjustAsync(id, new StockAdjustRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.NewQuantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, id, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
