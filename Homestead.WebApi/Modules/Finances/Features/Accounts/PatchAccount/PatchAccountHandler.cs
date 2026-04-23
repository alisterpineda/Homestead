using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;

public class PatchAccountHandler(
    AppDbContext db,
    IAuditContext audit,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<PatchAccountCommand, AccountResponse>
{
    public async Task<AccountResponse> Handle(PatchAccountCommand request, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (account is null)
            throw new EntityNotFoundException("Account not found.");

        if (account.IsSystem)
            throw new BusinessRuleException("System accounts cannot be modified.");

        audit.Track<AccountSnapshot, Account>(account, "Account", account.Id);

        var model = new PatchAccountModel
        {
            Name = account.Name,
            ParentId = account.ParentId,
            Institution = account.Institution,
            IsArchived = account.IsArchived,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (model.Institution is not null && model.Institution.Length > 128)
            throw new BusinessRuleException("Institution must be 128 characters or fewer.");

        if (model.ParentId == request.Id)
            throw new BusinessRuleException("An account cannot be its own parent.");

        if (model.ParentId is { } parentId)
        {
            var parentExists = await db.Accounts.AnyAsync(a => a.Id == parentId, ct);
            if (!parentExists)
                throw new BusinessRuleException("ParentId must reference an existing account.");
        }

        account.Name = model.Name.Trim();
        account.ParentId = model.ParentId;
        account.Institution = string.IsNullOrWhiteSpace(model.Institution) ? null : model.Institution.Trim();
        account.IsArchived = model.IsArchived;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        account.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(ct);

        return new AccountResponse
        {
            Id = account.Id,
            Name = account.Name,
            AccountType = account.AccountType,
            ParentId = account.ParentId,
            DefaultCommodityId = account.DefaultCommodityId,
            IsMultiPosition = account.IsMultiPosition,
            Institution = account.Institution,
            IsSystem = account.IsSystem,
            IsArchived = account.IsArchived,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
        };
    }
}
