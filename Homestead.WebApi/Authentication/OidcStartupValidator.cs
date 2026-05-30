using Homestead.WebApi.Common;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Authentication;

public class OidcStartupValidator(IOptions<OidcOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The build-time OpenAPI generator starts the host without real OIDC env vars, so skip the
        // requirement check there — otherwise it would fail `dotnet build` spec generation.
        if (BuildTimeOpenApi.IsGenerating)
            return Task.CompletedTask;

        if (!options.Value.IsConfigured)
            throw new InvalidOperationException(
                "OIDC configuration is required. Set OIDC_AUTHORITY, OIDC_CLIENT_ID, and OIDC_CLIENT_SECRET.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
