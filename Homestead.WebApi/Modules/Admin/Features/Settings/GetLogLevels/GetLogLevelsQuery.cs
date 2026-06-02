using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;

public record GetLogLevelsQuery(string? Category) : IRequest<LogLevelsResponse>;
