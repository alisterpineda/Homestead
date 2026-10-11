using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.PatchCategory;

public class PatchCategoryHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    ICategoryPathResolver pathResolver,
    IUnitOfWork unitOfWork,
    IValidator<PatchCategoryCommand> validator)
{
    public async Task<CategoryResponse> HandleAsync(PatchCategoryCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var category = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct);

        // Not a category → not found on this surface (consistent with the account CRUD hiding categories).
        if (category is null || !category.AccountType.IsCategory())
            throw new EntityNotFoundException("Category not found.");
        if (category.IsSystem)
            throw new BusinessRuleException("The Uncategorised bucket is read-only.");

        var name = request.Name.Trim();

        if (request.ParentId is Guid parentId)
        {
            if (parentId == category.Id)
                throw new BusinessRuleException("A category cannot be its own parent.");

            await CategoryWriteHelpers.ValidateParentAsync(db, parentId, category.AccountType, ct);

            // Cycle guard: the new parent must not be the category itself or any of its descendants.
            var descendants = await CategoryWriteHelpers.GetDescendantIdsAsync(db, category.Id, ct);
            if (descendants.Contains(parentId))
                throw new BusinessRuleException("A category cannot be moved under one of its own descendants.");
        }

        if (await CategoryWriteHelpers.SiblingNameTakenAsync(
                db, request.ParentId, category.AccountType, name, category.Id, ct))
            throw new DuplicateEntityException(
                $"A {category.AccountType} category named '{name}' already exists here.");

        audit.Track<AccountSnapshot, Account>(category, "Account", category.Id);

        category.Name = name;
        category.ParentAccountId = request.ParentId;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        // Paths are computed, not stored — reparenting recomputes every descendant's path for free.
        var map = await pathResolver.GetPathMapAsync(ct);
        var info = map.GetValueOrDefault(category.Id) ?? new CategoryPathInfo(category.Name, category.Name, 0);
        return CategoryMapping.ToResponse(
            category.Id, category.Name, category.AccountType, category.ParentAccountId, category.IsSystem, info);
    }
}
