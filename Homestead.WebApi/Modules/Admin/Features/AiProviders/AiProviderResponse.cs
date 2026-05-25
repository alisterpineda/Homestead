using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders;

public record AiProviderResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AiProviderType ProviderType { get; init; }
    public string? Configuration { get; init; }
    public required bool IsEnabled { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
}
