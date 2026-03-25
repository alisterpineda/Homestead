using Homestead.WebApi.Features.Admin.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.Settings.GetSystemSettings;

public record GetSystemSettingsQuery : IRequest<SystemSettingsResponse>;
