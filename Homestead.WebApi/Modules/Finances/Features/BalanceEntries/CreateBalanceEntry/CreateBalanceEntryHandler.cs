using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;

public class CreateBalanceEntryHandler(
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<CreateBalanceEntryCommand> validator)
{
    public async Task<BalanceEntryResponse> HandleAsync(CreateBalanceEntryCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

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
