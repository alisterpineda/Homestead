using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntry;

public record GetBalanceEntryQuery(Guid AccountId, Guid TransactionId) : IRequest<BalanceEntryResponse>;
