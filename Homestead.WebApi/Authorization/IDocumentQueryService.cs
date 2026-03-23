using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Authorization;

public interface IDocumentQueryService
{
    bool IsAdmin { get; }
    Task<bool> CanWriteToGroupAsync(Guid groupId, CancellationToken ct);
    IQueryable<Document> FilterByAccess(IQueryable<Document> query);
    IQueryable<Guid> AccessibleDocumentIds();

    /// <summary>
    /// Throws <see cref="Common.AccessDeniedException"/> if any of the given document IDs
    /// are inaccessible to the current user. Admins bypass this check.
    /// </summary>
    Task EnsureAllDocumentsAccessibleAsync(IQueryable<Guid> relatedDocumentIds, CancellationToken ct);

    /// <summary>
    /// Throws <see cref="Common.AccessDeniedException"/> if any of the given document IDs
    /// are not writable by the current user. Admins bypass this check.
    /// </summary>
    Task EnsureAllDocumentsWritableAsync(IQueryable<Guid> relatedDocumentIds, CancellationToken ct);

    IQueryable<Document> FilterByWriteAccess(IQueryable<Document> query);
}
