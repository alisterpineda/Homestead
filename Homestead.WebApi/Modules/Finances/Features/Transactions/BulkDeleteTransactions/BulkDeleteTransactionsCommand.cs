using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.BulkDeleteTransactions;

public record BulkDeleteTransactionsCommand(IReadOnlyList<Guid> Ids)
    : ICommand<BulkDeleteTransactionsResponse>;
