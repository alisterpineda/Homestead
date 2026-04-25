using Homestead.WebApi.Modules.Finances.Features.Transactions;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;

public record GetTransactionQuery(Guid TransactionId) : IRequest<TransactionResponse>;
