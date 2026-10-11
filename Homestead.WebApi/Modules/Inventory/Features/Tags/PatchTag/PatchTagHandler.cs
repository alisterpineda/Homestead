using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.PatchTag;

public class PatchTagHandler(AppDbContext db, IAuditContext audit,
    ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
{
    public async Task<PatchInventoryTagResponse> HandleAsync(Guid id, JsonPatchDocument<PatchInventoryTagModel> patchDoc, CancellationToken ct)
    {
        var tag = await db.InventoryTags
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tag is null)
            throw new EntityNotFoundException("Tag not found.");

        audit.Track<InventoryTagSnapshot, Tag>(tag, "InventoryTag", tag.Id);

        var oldPath = tag.Path;
        var oldName = tag.Name;
        var oldParentId = tag.ParentId;
        var oldEffectiveColor = tag.EffectiveColor;

        // Populate model from current DB state
        var model = new PatchInventoryTagModel
        {
            Name = tag.Name,
            ParentId = tag.ParentId,
            Color = tag.Color,
            Description = tag.Description
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

        // Validate name
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Tag name cannot be empty.");

        if (model.Name.Contains('/'))
            throw new BusinessRuleException("Tag name cannot contain '/' characters.");

        var normalizedName = model.Name.Trim();

        // Validate parent
        if (model.ParentId == id)
            throw new BusinessRuleException("A tag cannot be its own parent.");

        string? parentPath = null;
        string? newParentEffectiveColor = null;
        if (model.ParentId is not null)
        {
            var parent = await db.InventoryTags
                .FirstOrDefaultAsync(t => t.Id == model.ParentId, ct);

            if (parent is null)
                throw new BusinessRuleException("Parent tag not found.");

            // Check for circular reference: new parent must not be a descendant of this tag
            if (parent.Path.StartsWith(oldPath + "/") || parent.Path == oldPath)
                throw new BusinessRuleException("Cannot move a tag under one of its own descendants.");

            parentPath = parent.Path;
            newParentEffectiveColor = parent.EffectiveColor;
        }

        // Update entity from patched model
        tag.Name = normalizedName;
        tag.ParentId = model.ParentId;
        tag.Color = model.Color;
        tag.Description = model.Description;
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        tag.UpdatedByUserId = currentUser.UserId;

        // Recompute path if name or parent changed
        var nameChanged = normalizedName != oldName;
        var parentChanged = model.ParentId != oldParentId;

        tag.EffectiveColor = tag.Color ?? newParentEffectiveColor;
        var effectiveColorChanged = tag.EffectiveColor != oldEffectiveColor;

        List<Tag>? descendants = null;

        if (nameChanged || parentChanged)
        {
            var newPath = parentPath is not null
                ? parentPath + "/" + normalizedName
                : normalizedName;

            // Check for path uniqueness
            var conflictExists = await db.InventoryTags
                .AnyAsync(t => t.Path == newPath && t.Id != id, ct);

            if (conflictExists)
                throw new DuplicateEntityException($"A tag with path '{newPath}' already exists.");

            tag.Path = newPath;

            // Bulk-update all descendant paths (and reuse for color cascade)
            descendants = await db.InventoryTags
                .Where(t => t.Path.StartsWith(oldPath + "/"))
                .ToListAsync(ct);

            foreach (var descendant in descendants)
            {
                descendant.Path = newPath + descendant.Path[oldPath.Length..];
            }
        }

        if (effectiveColorChanged)
        {
            descendants ??= await db.InventoryTags
                .Where(t => t.Path.StartsWith(tag.Path + "/"))
                .ToListAsync(ct);

            InventoryEffectiveColorResolver.Recompute(tag, newParentEffectiveColor, descendants);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Load updater info
        string? updatedByUserName = null;
        if (tag.UpdatedByUserId.HasValue)
        {
            updatedByUserName = await db.Users
                .Where(u => u.Id == tag.UpdatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return new PatchInventoryTagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Path = tag.Path,
            ParentId = tag.ParentId,
            Color = tag.Color,
            EffectiveColor = tag.EffectiveColor,
            Description = tag.Description,
            UpdatedAt = tag.UpdatedAt,
            UpdatedByUserId = tag.UpdatedByUserId,
            UpdatedByUserName = updatedByUserName
        };
    }
}
