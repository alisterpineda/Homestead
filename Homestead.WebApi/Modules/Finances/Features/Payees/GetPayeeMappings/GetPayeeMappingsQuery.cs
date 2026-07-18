using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayeeMappings;

public record GetPayeeMappingsQuery(Guid PayeeId) : IRequest<IReadOnlyList<PayeeMappingResponse>>;
