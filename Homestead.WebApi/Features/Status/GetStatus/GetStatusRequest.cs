using MediatR;

namespace Homestead.WebApi.Features.Status.GetStatus;

public record GetStatusRequest : IRequest<GetStatusResponse>;
