using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Authorization;

public interface IDocumentQueryService
{
    bool IsAdmin { get; }
    Task<bool> CanWriteToGroupAsync(Guid groupId, CancellationToken ct);
    IQueryable<Document> FilterByAccess(IQueryable<Document> query);
    IQueryable<Guid> AccessibleDocumentIds();

    /// <summary>
    /// Documents readable by the current user, with the given trash scope. The blessed way to
    /// source a Document query: it owns the pairing of lifting the soft-delete filter (by name)
    /// with access filtering, so callers never touch <c>IgnoreQueryFilters</c> directly.
    /// </summary>
    IQueryable<Document> AccessibleDocuments(TrashFilter trash = TrashFilter.ActiveOnly);

    /// <summary>
    /// Documents writable by the current user, with the given trash scope.
    /// See <see cref="AccessibleDocuments"/>.
    /// </summary>
    IQueryable<Document> WritableDocuments(TrashFilter trash = TrashFilter.ActiveOnly);

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
