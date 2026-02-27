namespace Homestead.WebApi.Features.Documents.v1.UpdateTag;

public static class UpdateTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateTagRequest request,
            UpdateTagHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateTagCommand
            {
                Id = id,
                Color = request.Color
            };
            var response = await handler.HandleAsync(command, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateTag")
        .Produces<UpdateTagResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
