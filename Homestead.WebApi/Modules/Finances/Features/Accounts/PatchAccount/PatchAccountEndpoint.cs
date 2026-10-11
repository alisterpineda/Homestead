using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;

public static class PatchAccountEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchAccountModel> patchDoc,
            PatchAccountHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, patchDoc, ct)))
        .WithName("PatchAccount")
        .Accepts<JsonPatchDocument<PatchAccountModel>>("application/json-patch+json")
        .Produces<AccountResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
