using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.PatchFlow;

public class PatchFlowHandler(
    AppDbContext db,
    IFlowService flows,
    IBalanceEntryReconciler reconciler,
    ICategoryPathResolver pathResolver,
    IUnitOfWork unitOfWork,
    IValidator<PatchFlowCommand> validator)
{
    public async Task<TransactionResponse> HandleAsync(PatchFlowCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var result = await flows.UpdateAsync(
            request.TransactionId,
            new UpdateFlowRequest(
                request.AccountId,
                request.Amount,
                request.Date,
                request.Description,
                request.CategoryId,
                request.PayeeId),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        var categories = await pathResolver.GetPathMapAsync(ct);
        return TransactionMapping.ToResponse(
            result.Transaction!, categories,
            await DocumentLinkCounts.ForAsync(db, result.Transaction!.Id, ct),
            await PayeeNames.ForAsync(db, result.Transaction!, ct));
    }
}
