using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Authorization;

public class DocumentAccessService(
    ICurrentUserAccessor currentUser,
    AppDbContext db) : IDocumentAccessService
{
    public async Task<DocumentAccessResult> CheckAccessAsync(
        Guid documentId, DocumentOperation operation, CancellationToken ct)
    {
        var role = currentUser.Role;
        var userId = currentUser.UserId;

        // Admin always has full access
        if (role == AppRole.Admin)
            return new DocumentAccessResult(true, true);

        var document = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.GroupId })
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return new DocumentAccessResult(false, false);

        // Global document (GroupId is null)
        if (document.GroupId is null)
        {
            return role switch
            {
                AppRole.Contributor => new DocumentAccessResult(true, true),
                AppRole.Viewer => operation == DocumentOperation.Read
                    ? new DocumentAccessResult(true, false)
                    : new DocumentAccessResult(false, false),
                _ => new DocumentAccessResult(false, false) // Guest
            };
        }

        // Group-scoped document — check membership
        var canWrite = await HasGroupWriteAccessAsync(role, userId, document.GroupId.Value, ct);

        if (canWrite is null)
            return new DocumentAccessResult(false, false);

        if (operation == DocumentOperation.Write && !canWrite.Value)
            return new DocumentAccessResult(false, false);

        return new DocumentAccessResult(true, canWrite.Value);
    }

    public async Task<bool> CanWriteToGroupAsync(Guid groupId, CancellationToken ct)
    {
        var role = currentUser.Role;
        if (role == AppRole.Admin)
            return true;

        return await HasGroupWriteAccessAsync(role, currentUser.UserId, groupId, ct) ?? false;
    }

    /// <summary>
    /// Returns null if no access (no userId or not a member), otherwise returns whether the user can write.
    /// </summary>
    private async Task<bool?> HasGroupWriteAccessAsync(
        AppRole role, Guid? userId, Guid groupId, CancellationToken ct)
    {
        if (userId is null)
            return null;

        var membership = await db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == groupId && gm.UserId == userId.Value)
            .Select(gm => (GroupAccessLevel?)gm.AccessLevel)
            .FirstOrDefaultAsync(ct);

        if (membership is null)
            return null;

        var roleCanWrite = role is AppRole.Contributor;
        var groupCanWrite = membership is GroupAccessLevel.Owner or GroupAccessLevel.Editor;
        return roleCanWrite && groupCanWrite;
    }

    public IQueryable<Document> FilterByAccess(IQueryable<Document> query)
    {
        var role = currentUser.Role;
        var userId = currentUser.UserId;

        // Admin sees everything
        if (role == AppRole.Admin)
            return query;

        if (userId is null)
            return query.Where(d => false);

        var userGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == userId.Value)
            .Select(gm => gm.GroupId);

        // Guest: only group-scoped docs where they're a member
        if (role == AppRole.Guest)
        {
            return query.Where(d => d.GroupId != null && userGroupIds.Contains(d.GroupId.Value));
        }

        // Contributor/Viewer: global docs + group-scoped docs where they're a member
        return query.Where(d => d.GroupId == null || userGroupIds.Contains(d.GroupId!.Value));
    }
}
