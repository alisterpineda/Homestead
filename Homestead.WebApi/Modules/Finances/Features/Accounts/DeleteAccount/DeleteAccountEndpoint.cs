using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;

public static class DeleteAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteAccountHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.Handle(new DeleteAccountCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteAccount")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
