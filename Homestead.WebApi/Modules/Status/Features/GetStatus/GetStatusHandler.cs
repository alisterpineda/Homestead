using Homestead.WebApi.Authentication;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public class GetStatusHandler(IOptions<OidcOptions> oidcOptions)
{
    public Task<GetStatusResponse> HandleAsync(CancellationToken ct)
    {
        return Task.FromResult(new GetStatusResponse
        {
            DisplayName = oidcOptions.Value.DisplayName
        });
    }
}
