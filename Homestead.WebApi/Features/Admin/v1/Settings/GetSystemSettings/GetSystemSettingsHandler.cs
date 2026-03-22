using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings.GetSystemSettings;

public class GetSystemSettingsHandler
    : IRequestHandler<GetSystemSettingsRequest, SystemSettingsResponse>
{
    public Task<SystemSettingsResponse> Handle(GetSystemSettingsRequest request, CancellationToken ct)
    {
        return Task.FromResult(new SystemSettingsResponse());
    }
}
