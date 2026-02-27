namespace Homestead.WebApi.Features.Documents.v1.CreateTag;

public static class CreateTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateTagRequest request, CreateTagHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/documents/tags/{response.Id}", response);
        })
        .WithName("CreateTag")
        .Produces<CreateTagResponse>(StatusCodes.Status201Created);
    }
}
