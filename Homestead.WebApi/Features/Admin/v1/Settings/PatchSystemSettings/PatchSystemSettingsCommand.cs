using Homestead.WebApi.Features.Admin.v1.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchSystemSettings;

public record PatchSystemSettingsCommand : IRequest<SystemSettingsResponse>
{
}
