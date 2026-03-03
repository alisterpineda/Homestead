using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Authorization;

public class DocumentAuthorizationHandler(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<OperationAuthorizationRequirement, Guid>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Guid documentId)
    {
        var ct = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

        var role = CurrentUserAccessor.ParseRole(context.User);

        // Admin always has full access
        if (role == AppRole.Admin)
        {
            context.Succeed(requirement);
            return;
        }

        var userId = CurrentUserAccessor.ParseUserId(context.User);

        var document = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.GroupId })
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return;

        // Global document (GroupId is null)
        if (document.GroupId is null)
        {
            var allowed = role switch
            {
                AppRole.Contributor => true,
                AppRole.Viewer => requirement.Name == nameof(DocumentOperations.Read),
                _ => false
            };

            if (allowed)
                context.Succeed(requirement);

            return;
        }

        // Group-scoped document — check membership
        if (userId is null)
            return;

        var membership = await db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == document.GroupId.Value && gm.UserId == userId.Value)
            .Select(gm => (GroupAccessLevel?)gm.AccessLevel)
            .FirstOrDefaultAsync(ct);

        if (membership is null)
            return;

        var canWrite = role is AppRole.Contributor
            && membership is GroupAccessLevel.Owner or GroupAccessLevel.Editor;

        if (requirement.Name == nameof(DocumentOperations.Write) && !canWrite)
            return;

        context.Succeed(requirement);
    }
}
