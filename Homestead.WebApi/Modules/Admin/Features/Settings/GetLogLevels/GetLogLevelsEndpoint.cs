namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;

public static class GetLogLevelsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Single endpoint: the rule table is always returned; Resolved is populated when ?category= is
        // supplied. Keeps the generated client to one method.
        group.MapGet("/log-levels", async (string? category, GetLogLevelsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(category, ct)))
        .WithName("GetLogLevels")
        .Produces<LogLevelsResponse>();
    }
}
