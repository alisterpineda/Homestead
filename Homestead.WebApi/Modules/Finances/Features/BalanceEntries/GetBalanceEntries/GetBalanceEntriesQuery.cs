using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntries;

public record GetBalanceEntriesQuery(Guid AccountId) : IRequest<IReadOnlyList<BalanceEntryResponse>>;
