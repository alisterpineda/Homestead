using System.Reflection;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Authentication;

public class OidcStartupValidator(IOptions<OidcOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Skip validation during build-time OpenAPI document generation
        // (Microsoft.Extensions.ApiDescription.Server runs the app via GetDocument.Insider
        // without real OIDC env vars set).
        if (Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider")
            return Task.CompletedTask;

        if (!options.Value.IsConfigured)
            throw new InvalidOperationException(
                "OIDC configuration is required. Set OIDC_AUTHORITY, OIDC_CLIENT_ID, and OIDC_CLIENT_SECRET.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
