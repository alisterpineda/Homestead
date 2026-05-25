using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.GetAiProviders;

public record GetAiProvidersQuery : PaginatedRequest, IRequest<PaginatedResponse<AiProviderResponse>>
{
    public string? Search { get; init; }
}
