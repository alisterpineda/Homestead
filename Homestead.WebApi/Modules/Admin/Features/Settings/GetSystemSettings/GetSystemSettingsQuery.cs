using Homestead.WebApi.Modules.Admin.Features.Settings;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetSystemSettings;

public record GetSystemSettingsQuery : IRequest<SystemSettingsResponse>;
