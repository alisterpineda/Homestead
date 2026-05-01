using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;

public static class GetAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (
            [AsParameters] GetAccountQuery request,
            ISender sender,
            CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetAccount")
        .Produces<AccountResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
