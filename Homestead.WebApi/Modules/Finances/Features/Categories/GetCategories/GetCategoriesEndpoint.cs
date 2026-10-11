namespace Homestead.WebApi.Modules.Finances.Features.Categories.GetCategories;

public static class GetCategoriesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetCategoriesQuery request,
            GetCategoriesHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetCategories")
        .Produces<IReadOnlyList<CategoryResponse>>();
    }
}
