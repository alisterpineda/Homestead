using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;

public static class DeleteAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var deleted = await sender.Send(new DeleteAccountCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteAccount")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
