using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Authorization;

public interface IDocumentAccessService
{
    Task<DocumentAccessResult> CheckAccessAsync(Guid documentId, DocumentOperation operation, CancellationToken ct);
    Task<bool> CanWriteToGroupAsync(Guid groupId, CancellationToken ct);
    IQueryable<Document> FilterByAccess(IQueryable<Document> query);
}
