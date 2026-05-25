using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Documents.Features.AiProviderSummaries.GetAiProviderSummaries;

public record AiProviderSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AiProviderType ProviderType { get; init; }
    public required bool IsEnabled { get; init; }
}
