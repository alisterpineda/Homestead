using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupply;

public class GetSupplyHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetSupplyQuery, SupplyDetailResponse?>
{
    public Task<SupplyDetailResponse?> Handle(GetSupplyQuery request, CancellationToken ct) =>
        SupplyDetailReader.LoadAsync(db, locationResolver, request.Id, ct);
}
