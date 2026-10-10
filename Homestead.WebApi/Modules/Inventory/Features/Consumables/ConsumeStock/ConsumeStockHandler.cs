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
    public async Task<ConsumableDetailResponse> Handle(ConsumeStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.ConsumeAsync(request.ConsumableId, new StockConsumeRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.Quantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, request.ConsumableId, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
