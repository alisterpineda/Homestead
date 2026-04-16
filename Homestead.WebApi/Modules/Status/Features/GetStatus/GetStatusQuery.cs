using MediatR;

namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public record GetStatusQuery : IRequest<GetStatusResponse>;
