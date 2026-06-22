using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

public record GetAiStatusQuery : IRequest<AiStatusResponse>;
