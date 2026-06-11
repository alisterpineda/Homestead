using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.AdjustStock;

public class AdjustStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AdjustStockCommand, SupplyDetailResponse>
{
    public async Task<SupplyDetailResponse> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.AdjustAsync(request.SupplyId, new StockAdjustRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.NewQuantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await SupplyDetailReader.LoadAsync(db, locationResolver, request.SupplyId, ct)
            ?? throw new EntityNotFoundException("Supply not found.");
    }
}
