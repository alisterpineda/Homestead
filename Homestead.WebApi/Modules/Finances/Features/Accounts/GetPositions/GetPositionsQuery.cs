using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;

public record GetPositionsQuery(Guid AccountId) : IRequest<IReadOnlyList<PositionResponse>>;
