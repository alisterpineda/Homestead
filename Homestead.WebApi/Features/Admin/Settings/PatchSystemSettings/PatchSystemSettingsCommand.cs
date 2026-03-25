using Homestead.WebApi.Features.Admin.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.Settings.PatchSystemSettings;

public record PatchSystemSettingsCommand : IRequest<SystemSettingsResponse>
{
}
