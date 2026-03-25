using MediatR;

namespace Homestead.WebApi.Features.Admin.Settings.GetSystemSettings;

public class GetSystemSettingsHandler
    : IRequestHandler<GetSystemSettingsQuery, SystemSettingsResponse>
{
    public Task<SystemSettingsResponse> Handle(GetSystemSettingsQuery request, CancellationToken ct)
    {
        return Task.FromResult(new SystemSettingsResponse());
    }
}
