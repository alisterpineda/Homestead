using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;

public class CreateBalanceEntryHandler(
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateBalanceEntryCommand, BalanceEntryResponse>
{
    public async Task<BalanceEntryResponse> Handle(CreateBalanceEntryCommand request, CancellationToken ct)
    {
        var result = await balanceEntries.CreateAsync(
            new CreateBalanceEntryRequest(
                request.AccountId,
                request.CommodityId,
                request.AssertedAmount,
                request.Date,
                request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return BalanceEntryMapping.ToResponse(result.Entry!);
    }
}
