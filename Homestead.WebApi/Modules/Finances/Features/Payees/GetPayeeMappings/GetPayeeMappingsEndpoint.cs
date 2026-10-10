namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayeeMappings;

public static class GetPayeeMappingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/mappings", async (Guid id, GetPayeeMappingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(new GetPayeeMappingsQuery(id), ct));
        })
        .WithName("GetPayeeMappings")
        .Produces<IReadOnlyList<PayeeMappingResponse>>();
    }
}
