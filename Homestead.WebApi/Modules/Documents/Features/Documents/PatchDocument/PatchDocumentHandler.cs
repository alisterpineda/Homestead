using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;

public class PatchDocumentHandler(AppDbContext db,
    IAuditContext audit,
    IDocumentMetadataService metadataService, IDocumentQueryService queryService,
    ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
{
    // A patch op on one of these (lower-cased) paths is a manual edit of an AI-suggestible field, which
    // auto-declines that field's pending suggestions. Every DocumentSuggestionField needs an entry
    // (SuggestionFieldCoverageTests).
    internal static readonly IReadOnlyDictionary<string, DocumentSuggestionField> SuggestionFieldsByPatchPath =
        new Dictionary<string, DocumentSuggestionField>
    {
        ["tagpaths"] = DocumentSuggestionField.Tags,
        ["partyids"] = DocumentSuggestionField.Parties,
        ["name"] = DocumentSuggestionField.Name,
        ["documentdate"] = DocumentSuggestionField.DocumentDate,
        ["expirydate"] = DocumentSuggestionField.ExpiryDate
    };

    public async Task<PatchDocumentResponse> HandleAsync(Guid id, JsonPatchDocument<PatchDocumentModel> patchDoc, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (document is null)
            throw new EntityNotFoundException("Document not found.");

        audit.Track<DocumentSnapshot, Document>(document, "Document", document.Id);

        // Populate model from current DB state
        var model = new PatchDocumentModel
        {
            Name = document.Name,
            DocumentDate = document.DocumentDate,
            ExpiryDate = document.ExpiryDate,
            Notes = document.Notes,
            TagPaths = document.DocumentTags.Select(dt => dt.Tag.Path).ToList(),
            PartyIds = document.DocumentParties.Select(dp => dp.PartyId).ToList(),
            GroupId = document.GroupId
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

        // Validate
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");

        // Validate party IDs
        await metadataService.ValidatePartyIdsAsync(model.PartyIds, ct);

        // Validate GroupId if changed
        if (model.GroupId.HasValue && model.GroupId != document.GroupId)
        {
            var groupExists = await db.Groups.AnyAsync(g => g.Id == model.GroupId.Value, ct);
            if (!groupExists)
                throw new BusinessRuleException("Group not found.");

            var canWriteToGroup = await queryService.CanWriteToGroupAsync(model.GroupId.Value, ct);
            if (!canWriteToGroup)
                throw new AccessDeniedException();
        }

        // Update entity from patched model
        document.Name = model.Name;
        document.DocumentDate = model.DocumentDate;
        document.ExpiryDate = model.ExpiryDate;
        document.Notes = model.Notes;
        document.GroupId = model.GroupId;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        document.UpdatedByUserId = currentUser.UserId;

        // Update tags
        var resolvedTags = await metadataService.ResolveTagPathsAsync(model.TagPaths, ct);
        metadataService.SetTags(document, resolvedTags);

        // Update parties
        metadataService.SetParties(document, model.PartyIds);

        // Clear pending suggestions for fields the user explicitly changed
        var patchPaths = patchDoc.Operations
            .Select(op => op.path?.TrimStart('/').ToLowerInvariant())
            .Where(p => p is not null)
            .ToHashSet();

        var changedFields = SuggestionFieldsByPatchPath
            .Where(kv => patchPaths.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToHashSet();

        foreach (var s in document.Suggestions.Where(s => changedFields.Contains(s.Field)))
        {
            s.Status = DocumentSuggestionStatus.Declined;
            s.ResolvedAt = DateTimeOffset.UtcNow;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Load tags for response
        var tags = await db.DocumentTags
            .AsNoTracking()
            .Where(dt => dt.DocumentId == document.Id)
            .Select(dt => new TagDto
            {
                Id = dt.Tag.Id,
                Name = dt.Tag.Name,
                Path = dt.Tag.Path,
                Color = dt.Tag.Color,
                EffectiveColor = dt.Tag.EffectiveColor
            })
            .ToListAsync(ct);

        // Load parties for response
        var parties = await db.DocumentParties
            .AsNoTracking()
            .Where(dp => dp.DocumentId == document.Id)
            .Select(dp => new PartyDto
            {
                Id = dp.Party.Id,
                Name = dp.Party.Name,
                Description = dp.Party.Description
            })
            .ToListAsync(ct);

        // Load owner info for response
        string? createdByUserName = null;
        string? ownerUserName = null;
        if (document.CreatedByUserId.HasValue)
        {
            createdByUserName = await db.Users
                .Where(u => u.Id == document.CreatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }
        if (document.OwnerUserId.HasValue)
        {
            ownerUserName = await db.Users
                .Where(u => u.Id == document.OwnerUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        // Load group info
        string? groupName = null;
        if (document.GroupId.HasValue)
        {
            groupName = await db.Groups
                .Where(g => g.Id == document.GroupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(ct);
        }

        // Load latest version for file fields
        var latestVersion = await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == document.Id)
            .OrderByDescending(r => r.VersionNumber)
            .FirstAsync(ct);

        // Load updater info
        string? updatedByUserName = null;
        if (document.UpdatedByUserId.HasValue)
        {
            updatedByUserName = await db.Users
                .Where(u => u.Id == document.UpdatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return new PatchDocumentResponse
        {
            Id = document.Id,
            Name = document.Name,
            ContentType = latestVersion.ContentType,
            FileSize = latestVersion.FileSize,
            FileHash = latestVersion.FileHash,
            FileExtension = latestVersion.FileExtension,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            UpdatedByUserId = document.UpdatedByUserId,
            UpdatedByUserName = updatedByUserName,
            DocumentDate = document.DocumentDate,
            ExpiryDate = document.ExpiryDate,
            Notes = document.Notes,
            Summary = document.Summary,
            CreatedByUserId = document.CreatedByUserId,
            CreatedByUserName = createdByUserName,
            OwnerUserId = document.OwnerUserId,
            OwnerUserName = ownerUserName,
            GroupId = document.GroupId,
            GroupName = groupName,
            Tags = tags,
            Parties = parties
        };
    }

}
