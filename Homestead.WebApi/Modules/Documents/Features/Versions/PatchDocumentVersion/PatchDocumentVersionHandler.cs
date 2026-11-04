using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.PatchDocumentVersion;

public class PatchDocumentVersionHandler(AppDbContext db,
    IAuditContext audit, ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
{
    public async Task<PatchDocumentVersionResponse> HandleAsync(Guid documentId, Guid id,
        JsonPatchDocument<PatchDocumentVersionModel> patchDoc, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == documentId && r.Id == id)
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
        patchDoc.ApplyTo(model, error => errors.Add(error));

        if (errors.Count > 0)
            throw new BusinessRuleException(errors.Select(e => new
            {
                Operation = e.Operation?.ToString(),
                e.ErrorMessage
            }));

        // Update entity from patched model
        version.Label = model.Label;

        var now = DateTimeOffset.UtcNow;

        // Hand-editing the text (clearing it included) replaces its provenance: the engine's record
        // describes text this row no longer holds, so attributing the edit to Tesseract or Azure, page
        // counts and all, would mislead — but erasing it would make the version read as never extracted.
        // The manual record names the editor and keeps the engine run as its basis.
        if (model.ExtractedText != version.ExtractedText)
        {
            var previous = TextExtractionMetadata.Deserialize(version.TextExtractionMetadata);
            version.TextExtractionMetadata = TextExtractionMetadata.Serialize(
                ManualTextExtractionMetadata.Replacing(previous, currentUser.UserId, now));
        }

        version.ExtractedText = model.ExtractedText;
        version.UpdatedAt = now;

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
