using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.CreateCategory;

public class CreateCategoryHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    ICategoryPathResolver pathResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCategoryCommand, CategoryResponse>
{
    public async Task<CategoryResponse> Handle(CreateCategoryCommand request, CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (request.ParentId is Guid parentId)
            await CategoryWriteHelpers.ValidateParentAsync(db, parentId, request.Type, ct);

        if (await CategoryWriteHelpers.SiblingNameTakenAsync(db, request.ParentId, request.Type, name, null, ct))
            throw new DuplicateEntityException(
                $"A {request.Type} category named '{name}' already exists here.");

        var category = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = request.Type,
            Kind = null,
            ParentAccountId = request.ParentId,
            // Required-but-meaningless FK for a multi-currency category — set internally, never surfaced.
            ReportingCurrencyId = await ResolveReportingCurrencyAsync(ct),
            IsSystem = false,
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Accounts.Add(category);
        audit.Created("Account", category.Id);
        await unitOfWork.SaveChangesAsync(ct);

        var map = await pathResolver.GetPathMapAsync(ct);
        var info = map.GetValueOrDefault(category.Id) ?? new CategoryPathInfo(category.Name, category.Name, 0);
        return CategoryMapping.ToResponse(
            category.Id, category.Name, category.AccountType, category.ParentAccountId, category.IsSystem, info);
    }

    // Same default the SystemAccountSeeder resolves: the household display currency, else the seeded USD
    // currency, else any currency. A category's reporting currency is never read — this only satisfies the FK.
    private async Task<Guid> ResolveReportingCurrencyAsync(CancellationToken ct)
    {
        var settings = await FinanceSettings.LoadAsync(db, ct);
        if (settings.DisplayCurrencyId is Guid displayId)
            return displayId;

        var fallback = await db.Commodities
            .Where(c => c.Kind == CommodityKind.Currency)
            .OrderByDescending(c => c.Symbol == "USD")
            .ThenBy(c => c.Symbol)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        return fallback ?? throw new BusinessRuleException("No currency is configured; add a currency first.");
    }
}
