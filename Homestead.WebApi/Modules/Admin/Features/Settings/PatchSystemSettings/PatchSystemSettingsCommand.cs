using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Admin.Features.Settings;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public record PatchSystemSettingsCommand : ICommand<SystemSettingsResponse>
{
}
