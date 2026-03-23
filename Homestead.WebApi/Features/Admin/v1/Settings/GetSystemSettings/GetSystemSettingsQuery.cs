using Homestead.WebApi.Features.Admin.v1.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings.GetSystemSettings;

public record GetSystemSettingsQuery : IRequest<SystemSettingsResponse>;
