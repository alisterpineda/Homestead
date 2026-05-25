using Homestead.WebApi.Domain.Settings;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.CreateAiProvider;

public record CreateAiProviderCommand : IRequest<AiProviderResponse>
{
    public required string Name { get; init; }
    public required AiProviderType ProviderType { get; init; }
    public string? Configuration { get; init; }
}
