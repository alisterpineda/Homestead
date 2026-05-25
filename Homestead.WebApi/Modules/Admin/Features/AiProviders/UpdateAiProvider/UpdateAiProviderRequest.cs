namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.UpdateAiProvider;

public record UpdateAiProviderRequest
{
    public required string Name { get; init; }
    public string? Configuration { get; init; }
    public bool IsEnabled { get; init; } = true;
}
