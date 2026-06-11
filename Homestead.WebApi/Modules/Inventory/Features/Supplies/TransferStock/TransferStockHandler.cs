using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.TransferStock;

public class TransferStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TransferStockCommand, SupplyDetailResponse>
{
    public async Task<SupplyDetailResponse> Handle(TransferStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.TransferAsync(request.SupplyId, new StockTransferRequest(
            new StockLocation(body.FromAreaId, body.FromContainerItemId),
            new StockLocation(body.ToAreaId, body.ToContainerItemId),
            body.Quantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await SupplyDetailReader.LoadAsync(db, locationResolver, request.SupplyId, ct)
            ?? throw new EntityNotFoundException("Supply not found.");
    }
}
