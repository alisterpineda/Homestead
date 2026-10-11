using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.CreateTag;

public static class CreateTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateTagCommand request, CreateTagHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/documents/tags/{response.Id}", response);
        })
        .WithName("CreateTag")
        .Produces<CreateTagResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
