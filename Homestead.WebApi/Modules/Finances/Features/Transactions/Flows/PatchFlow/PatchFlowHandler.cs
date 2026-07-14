using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.PatchFlow;

public class PatchFlowHandler(
    IFlowService flows,
    IBalanceEntryReconciler reconciler,
    ICategoryPathResolver pathResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchFlowCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(PatchFlowCommand request, CancellationToken ct)
    {
        var result = await flows.UpdateAsync(
            request.TransactionId,
            new UpdateFlowRequest(
                request.AccountId,
                request.Amount,
                request.Date,
                request.Description,
                request.CategoryId),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        var categories = await pathResolver.GetPathMapAsync(ct);
        return TransactionMapping.ToResponse(result.Transaction!, categories);
    }
}
