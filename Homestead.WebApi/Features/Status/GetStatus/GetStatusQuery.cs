using MediatR;

namespace Homestead.WebApi.Features.Status.GetStatus;

public record GetStatusQuery : IRequest<GetStatusResponse>;
