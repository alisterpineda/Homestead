using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

public interface IAiClientFactory
{
    // True when an AI endpoint + model are statically configured. Callers gate on this for the graceful
    // no-op when AI is unconfigured, rather than calling CreateClient (which throws).
    bool IsConfigured { get; }

    IChatClient CreateClient();
}
