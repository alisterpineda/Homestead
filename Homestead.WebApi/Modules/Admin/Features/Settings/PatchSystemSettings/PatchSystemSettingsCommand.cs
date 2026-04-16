using Homestead.WebApi.Modules.Admin.Features.Settings;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public record PatchSystemSettingsCommand : IRequest<SystemSettingsResponse>
{
}
