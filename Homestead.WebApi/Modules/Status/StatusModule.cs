using Homestead.WebApi.Modules.Status.Features.GetStatus;

namespace Homestead.WebApi.Modules.Status;

public static class StatusModule
{
    public static WebApplication MapStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/status").WithTags("Status");

        GetStatusEndpoint.Map(group);

        return app;
    }
}
