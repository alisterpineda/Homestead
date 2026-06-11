using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.ConsumeStock;

public class ConsumeStockHandler(
    AppDbContext db,
    IStockService stock,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ConsumeStockCommand, SupplyDetailResponse>
{
    public async Task<SupplyDetailResponse> Handle(ConsumeStockCommand request, CancellationToken ct)
    {
        var body = request.Body;
        await stock.ConsumeAsync(request.SupplyId, new StockConsumeRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.Quantity, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await SupplyDetailReader.LoadAsync(db, locationResolver, request.SupplyId, ct)
            ?? throw new EntityNotFoundException("Supply not found.");
    }
}
