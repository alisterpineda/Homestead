using Homestead.WebApi.Common.Logging;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;

public class GetLogLevelsHandler(ILogLevelInspector inspector)
    : IRequestHandler<GetLogLevelsQuery, LogLevelsResponse>
{
    public Task<LogLevelsResponse> Handle(GetLogLevelsQuery request, CancellationToken ct)
    {
        var table = inspector.GetTable();
        // Reuse the table just built for the resolution rather than letting Resolve rebuild it.
        var resolved = string.IsNullOrWhiteSpace(request.Category)
            ? null
            : inspector.Resolve(request.Category, table.Rules);

        return Task.FromResult(new LogLevelsResponse
        {
            MinLevel = table.MinLevel,
            Rules = table.Rules,
            Resolved = resolved,
        });
    }
}
