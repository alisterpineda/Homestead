using MediatR;

namespace Homestead.WebApi.Features.Admin.Groups.CreateGroup;

public static class CreateGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateGroupCommand request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/admin/groups/{response.Id}", response);
        })
        .WithName("CreateGroup")
        .Produces<GroupResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict);
    }
}
