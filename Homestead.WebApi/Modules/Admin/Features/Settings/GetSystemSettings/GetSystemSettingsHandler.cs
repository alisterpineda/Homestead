using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetSystemSettings;

public class GetSystemSettingsHandler
    : IRequestHandler<GetSystemSettingsQuery, SystemSettingsResponse>
{
    public Task<SystemSettingsResponse> Handle(GetSystemSettingsQuery request, CancellationToken ct)
    {
        return Task.FromResult(new SystemSettingsResponse());
    }
}
