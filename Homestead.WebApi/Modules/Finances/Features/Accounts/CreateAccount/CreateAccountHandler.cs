using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public class CreateAccountHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit)
    : IRequestHandler<CreateAccountCommand, AccountResponse>
{
    public async Task<AccountResponse> Handle(CreateAccountCommand request, CancellationToken ct)
    {
        var commodityExists = await db.Commodities.AnyAsync(c => c.Id == request.DefaultCommodityId, ct);
        if (!commodityExists)
            throw new BusinessRuleException("DefaultCommodityId must reference an existing commodity.");

        if (request.ParentId is { } parentId)
        {
            var parentExists = await db.Accounts.AnyAsync(a => a.Id == parentId, ct);
            if (!parentExists)
                throw new BusinessRuleException("ParentId must reference an existing account.");
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            AccountType = request.AccountType,
            ParentId = request.ParentId,
            DefaultCommodityId = request.DefaultCommodityId,
            IsMultiPosition = request.IsMultiPosition,
            Institution = string.IsNullOrWhiteSpace(request.Institution) ? null : request.Institution.Trim(),
            IsSystem = false,
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Accounts.Add(account);
        audit.Created("Account", account.Id);
        await db.SaveChangesAsync(ct);

        return ToResponse(account);
    }

    private static AccountResponse ToResponse(Account a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        AccountType = a.AccountType,
        ParentId = a.ParentId,
        DefaultCommodityId = a.DefaultCommodityId,
        IsMultiPosition = a.IsMultiPosition,
        Institution = a.Institution,
        IsSystem = a.IsSystem,
        IsArchived = a.IsArchived,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };
}
