using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public static class CreateAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateAccountCommand request, CreateAccountHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/finances/accounts/{response.Id}", response);
        })
        .WithName("CreateAccount")
        .Produces<AccountResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
