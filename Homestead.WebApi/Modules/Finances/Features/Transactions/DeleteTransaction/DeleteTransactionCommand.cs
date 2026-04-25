using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;

public record DeleteTransactionCommand(Guid TransactionId) : IRequest<Unit>;
