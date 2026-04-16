using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public class GetStatusHandler(IOptions<OidcOptions> oidcOptions)
    : IRequestHandler<GetStatusQuery, GetStatusResponse>
{
    public Task<GetStatusResponse> Handle(GetStatusQuery request, CancellationToken ct)
    {
        return Task.FromResult(new GetStatusResponse
        {
            DisplayName = oidcOptions.Value.DisplayName
        });
    }
}
