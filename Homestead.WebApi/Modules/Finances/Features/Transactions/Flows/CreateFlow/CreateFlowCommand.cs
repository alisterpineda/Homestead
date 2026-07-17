using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Transactions;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.CreateFlow;

public record CreateFlowCommand : ICommand<TransactionResponse>
{
    public required FlowDirection Direction { get; init; }
    public required Guid AccountId { get; init; }
    public required decimal Amount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Description { get; init; }

    // null → Uncategorised (posts to the system Expense/Income bucket).
    public Guid? CategoryId { get; init; }

    // null → no payee. Archived payees are accepted (archiving only hides from pickers).
    public Guid? PayeeId { get; init; }
}
