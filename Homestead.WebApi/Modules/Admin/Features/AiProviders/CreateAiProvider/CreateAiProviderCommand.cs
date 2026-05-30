using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.CreateAiProvider;

public record CreateAiProviderCommand : ICommand<AiProviderResponse>
{
    public required string Name { get; init; }
    public required AiProviderType ProviderType { get; init; }
    public string? Configuration { get; init; }
}
