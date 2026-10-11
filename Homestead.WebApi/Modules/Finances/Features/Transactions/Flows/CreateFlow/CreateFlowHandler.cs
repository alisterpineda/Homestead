using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.CreateFlow;

public class CreateFlowHandler(
    AppDbContext db,
    IFlowService flows,
    IBalanceEntryReconciler reconciler,
    ICategoryPathResolver pathResolver,
    IUnitOfWork unitOfWork,
    IValidator<CreateFlowCommand> validator)
{
    public async Task<TransactionResponse> HandleAsync(CreateFlowCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var result = await flows.CreateAsync(
            new CreateFlowRequest(
                request.Direction,
                request.AccountId,
                request.Amount,
                request.Date,
                request.Description,
                request.CategoryId,
                request.PayeeId),
            ct);

        // Flush the primary change so the reconciler's DB queries see the new flow.
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        var categories = await pathResolver.GetPathMapAsync(ct);
        return TransactionMapping.ToResponse(
            result.Transaction!, categories,
            payeeNames: await PayeeNames.ForAsync(db, result.Transaction!, ct));
    }
}
