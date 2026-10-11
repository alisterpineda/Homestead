namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;

public static class GetAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (
            [AsParameters] GetAccountQuery request,
            GetAccountHandler handler,
            CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetAccount")
        .Produces<AccountResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
