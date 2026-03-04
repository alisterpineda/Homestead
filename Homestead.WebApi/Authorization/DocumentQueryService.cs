using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Authorization;

public class DocumentQueryService(
    ICurrentUserAccessor currentUser,
    AppDbContext db) : IDocumentQueryService
{
    public bool IsAdmin => currentUser.Role == AppRole.Admin;

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

    public IQueryable<Guid> AccessibleDocumentIds()
        => FilterByAccess(db.Documents).Select(d => d.Id);

    public async Task EnsureAllDocumentsAccessibleAsync(
        IQueryable<Guid> relatedDocumentIds, CancellationToken ct)
        => await EnsureAllDocumentsInSetAsync(AccessibleDocumentIds(), relatedDocumentIds, ct);

    public async Task EnsureAllDocumentsWritableAsync(
        IQueryable<Guid> relatedDocumentIds, CancellationToken ct)
        => await EnsureAllDocumentsInSetAsync(WritableDocumentIds(), relatedDocumentIds, ct);

    private IQueryable<Guid> WritableDocumentIds()
    {
        var role = currentUser.Role;
        var userId = currentUser.UserId;

        if (role == AppRole.Admin)
            return db.Documents.Select(d => d.Id);

        if (role != AppRole.Contributor || userId is null)
            return db.Documents.Where(d => false).Select(d => d.Id);

        // Contributor: global docs + group-scoped docs where user is Owner/Editor
        var writableGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == userId.Value
                && (gm.AccessLevel == GroupAccessLevel.Owner || gm.AccessLevel == GroupAccessLevel.Editor))
            .Select(gm => gm.GroupId);

        return db.Documents
            .Where(d => d.GroupId == null || writableGroupIds.Contains(d.GroupId!.Value))
            .Select(d => d.Id);
    }

    private async Task EnsureAllDocumentsInSetAsync(
        IQueryable<Guid> allowedDocIds, IQueryable<Guid> relatedDocumentIds, CancellationToken ct)
    {
        if (IsAdmin)
            return;

        var hasDisallowed = await relatedDocumentIds
            .AnyAsync(docId => !allowedDocIds.Contains(docId), ct);

        if (hasDisallowed)
            throw new ForbiddenAccessException();
    }
}
