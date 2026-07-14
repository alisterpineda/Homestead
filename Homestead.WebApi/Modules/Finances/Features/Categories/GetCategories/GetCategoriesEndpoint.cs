using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.GetCategories;

public static class GetCategoriesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetCategoriesQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetCategories")
        .Produces<IReadOnlyList<CategoryResponse>>();
    }
}
