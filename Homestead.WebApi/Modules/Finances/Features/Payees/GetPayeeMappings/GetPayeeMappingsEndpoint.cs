namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayeeMappings;

public static class GetPayeeMappingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/mappings", async (Guid id, GetPayeeMappingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, ct)))
        .WithName("GetPayeeMappings")
        .Produces<IReadOnlyList<PayeeMappingResponse>>();
    }
}
