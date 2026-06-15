using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.AdjustStock;

public class AdjustStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AdjustStockCommand, ConsumableDetailResponse>
{
    public async Task<ConsumableDetailResponse> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.AdjustAsync(request.ConsumableId, new StockAdjustRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.NewQuantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, request.ConsumableId, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
