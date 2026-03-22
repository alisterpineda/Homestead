using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocumentVersion;

public class PatchDocumentVersionHandler(AppDbContext db,
    IAuditLogger auditLogger, ISearchVectorUpdater searchVectorUpdater,
    IAuthorizationService authService, IHttpContextAccessor httpContext)
    : IRequestHandler<PatchDocumentVersionCommand, PatchDocumentVersionResponse>
{
    public async Task<PatchDocumentVersionResponse> Handle(PatchDocumentVersionCommand request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Write);

        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            throw new NotFoundException("Document version not found.");

        var before = DocumentVersionSnapshot.From(version);

        // Populate model from current DB state
        var model = new PatchDocumentVersionModel
        {
            Label = version.Label,
            ExtractedText = version.ExtractedText
        };

        // Apply patch operations
        var errors = new List<JsonPatchError>();
        request.PatchDocument.ApplyTo(model, error => errors.Add(error));

        if (errors.Count > 0)
            throw new BadRequestException(errors.Select(e => new
            {
                Operation = e.Operation?.ToString(),
                e.ErrorMessage
            }));

        // Update entity from patched model
        version.Label = model.Label;
        version.ExtractedText = model.ExtractedText;

        var after = DocumentVersionSnapshot.From(version);
        var changes = before.Diff(after);
        auditLogger.LogUpdated("DocumentVersion", version.Id, changes,
            parentEntityId: version.DocumentId);

        await db.SaveChangesAsync(ct);

        await searchVectorUpdater.UpdateSearchVectorAsync(version.DocumentId, ct);

        return new PatchDocumentVersionResponse
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            Label = version.Label,
            ContentType = version.ContentType,
            FileSize = version.FileSize,
            FileExtension = version.FileExtension,
            CreatedAt = version.CreatedAt
        };
    }
}
