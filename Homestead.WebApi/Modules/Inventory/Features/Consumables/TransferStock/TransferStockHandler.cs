using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TransferStock;

public class TransferStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
{
    public async Task<ConsumableDetailResponse> HandleAsync(Guid id, TransferStockRequest body, CancellationToken ct)
    {
        await stock.TransferAsync(id, new StockTransferRequest(
            new StockLocation(body.FromAreaId, body.FromContainerItemId),
            new StockLocation(body.ToAreaId, body.ToContainerItemId),
            body.Quantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, id, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
