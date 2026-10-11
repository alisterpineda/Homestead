using Homestead.WebApi.Common.Logging;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;

public class GetLogLevelsHandler(ILogLevelInspector inspector)
{
    public Task<LogLevelsResponse> HandleAsync(string? category, CancellationToken ct)
    {
        var table = inspector.GetTable();
        // Reuse the table just built for the resolution rather than letting Resolve rebuild it.
        var resolved = string.IsNullOrWhiteSpace(category)
            ? null
            : inspector.Resolve(category, table.Rules);

        return Task.FromResult(new LogLevelsResponse
        {
            MinLevel = table.MinLevel,
            Rules = table.Rules,
            Resolved = resolved,
        });
    }
}
