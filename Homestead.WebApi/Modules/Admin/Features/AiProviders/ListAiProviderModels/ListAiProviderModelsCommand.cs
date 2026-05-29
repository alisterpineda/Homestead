using Homestead.WebApi.Domain.Settings;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.ListAiProviderModels;

public record ListAiProviderModelsRequest
{
    public AiProviderType? ProviderType { get; init; }
    public string? Configuration { get; init; }
}

public record ListAiProviderModelsCommand : IRequest<ListModelsResponse>
{
    public Guid? Id { get; init; }
    public AiProviderType? ProviderType { get; init; }
    public string? Configuration { get; init; }
}
