using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.ConsumeStock;

public class ConsumeStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
{
    public async Task<ConsumableDetailResponse> HandleAsync(Guid id, ConsumeStockRequest body, CancellationToken ct)
    {
        await stock.ConsumeAsync(id, new StockConsumeRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.Quantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, id, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
