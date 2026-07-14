using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.CreateFlow;

public class CreateFlowHandler(
    IFlowService flows,
    IBalanceEntryReconciler reconciler,
    ICategoryPathResolver pathResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateFlowCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(CreateFlowCommand request, CancellationToken ct)
    {
        var result = await flows.CreateAsync(
            new CreateFlowRequest(
                request.Direction,
                request.AccountId,
                request.Amount,
                request.Date,
                request.Description,
                request.CategoryId),
            ct);

        // Flush the primary change so the reconciler's DB queries see the new flow.
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        var categories = await pathResolver.GetPathMapAsync(ct);
        return TransactionMapping.ToResponse(result.Transaction!, categories);
    }
}
