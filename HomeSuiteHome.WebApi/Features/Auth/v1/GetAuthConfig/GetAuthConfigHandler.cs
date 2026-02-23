using HomeSuiteHome.WebApi.Authentication;
using HomeSuiteHome.WebApi.Common;
using Microsoft.Extensions.Options;

namespace HomeSuiteHome.WebApi.Features.Auth.v1.GetAuthConfig;

public class GetAuthConfigHandler(IOptions<AuthOptions> authOptions)
    : IQueryHandler<GetAuthConfigRequest, GetAuthConfigResponse>
{
    public Task<GetAuthConfigResponse> HandleAsync(GetAuthConfigRequest request, CancellationToken ct)
    {
        var options = authOptions.Value;
        return Task.FromResult(new GetAuthConfigResponse
        {
            Authority = options.Authority,
            ClientId = options.Audience
        });
    }
}
