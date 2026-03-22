using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings;

public record PatchSystemSettingsRequest : IRequest<SystemSettingsResponse>
{
}
