using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.ReceiveStock;

public class ReceiveStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ReceiveStockCommand, SupplyDetailResponse>
{
    public async Task<SupplyDetailResponse> Handle(ReceiveStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.ReceiveAsync(request.SupplyId, new StockReceiveRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.Quantity, body.UnitCost, body.UnitCostCurrencyId, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await SupplyDetailReader.LoadAsync(db, locationResolver, request.SupplyId, ct)
            ?? throw new EntityNotFoundException("Supply not found.");
    }
}
