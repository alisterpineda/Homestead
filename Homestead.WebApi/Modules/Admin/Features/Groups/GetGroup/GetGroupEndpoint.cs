namespace Homestead.WebApi.Modules.Admin.Features.Groups.GetGroup;

public static class GetGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetGroupHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetGroupQuery(id), ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetGroup")
        .Produces<GroupDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
