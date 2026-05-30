using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Common.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.PatchDocumentVersion;

public class PatchDocumentVersionHandler(AppDbContext db,
    IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<PatchDocumentVersionCommand, PatchDocumentVersionResponse>
{
    public async Task<PatchDocumentVersionResponse> Handle(PatchDocumentVersionCommand request, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            throw new EntityNotFoundException("Document version not found.");

        audit.Track<DocumentVersionSnapshot, DocumentVersion>(version, "DocumentVersion", version.Id,
            parentEntityId: version.DocumentId);

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
            throw new BusinessRuleException(errors.Select(e => new
            {
                Operation = e.Operation?.ToString(),
                e.ErrorMessage
            }));

        // Update entity from patched model
        version.Label = model.Label;
        version.ExtractedText = model.ExtractedText;
        version.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

        return new PatchDocumentVersionResponse
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            Label = version.Label,
            ContentType = version.ContentType,
            FileSize = version.FileSize,
            FileExtension = version.FileExtension,
            CreatedAt = version.CreatedAt,
            UpdatedAt = version.UpdatedAt
        };
    }
}
