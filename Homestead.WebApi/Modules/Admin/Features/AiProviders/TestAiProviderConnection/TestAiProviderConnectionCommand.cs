using Homestead.WebApi.Domain.Settings;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.TestAiProviderConnection;

// Request body shared by both routes. The ad-hoc route requires ProviderType; the {id} route
// ignores it (the stored provider's type is used) and treats Configuration as optional.
public record TestAiProviderConnectionRequest
{
    public AiProviderType? ProviderType { get; init; }
    public string? Configuration { get; init; }
}

public record TestAiProviderConnectionCommand : IRequest<TestConnectionResponse>
{
    public Guid? Id { get; init; }
    public AiProviderType? ProviderType { get; init; }
    public string? Configuration { get; init; }
}
