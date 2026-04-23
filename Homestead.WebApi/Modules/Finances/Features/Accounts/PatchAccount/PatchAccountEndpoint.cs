using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;

public static class PatchAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchAccountModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchAccountCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchAccount")
        .Accepts<JsonPatchDocument<PatchAccountModel>>("application/json-patch+json")
        .Produces<AccountResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
