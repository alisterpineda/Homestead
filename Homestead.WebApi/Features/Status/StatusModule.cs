using Homestead.WebApi.Features.Status.GetStatus;

namespace Homestead.WebApi.Features.Status;

public static class StatusModule
{
    public static WebApplication MapStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/status").WithTags("Status");

        GetStatusEndpoint.Map(group);

        return app;
    }
}
