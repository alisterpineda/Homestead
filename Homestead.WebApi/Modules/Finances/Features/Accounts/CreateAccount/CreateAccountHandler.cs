using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public class CreateAccountHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IOpeningBalanceService openingBalances)
    : IRequestHandler<CreateAccountCommand, AccountResponse>
{
    public async Task<AccountResponse> Handle(CreateAccountCommand request, CancellationToken ct)
    {
        var reportingCurrency = await db.Commodities
            .FirstOrDefaultAsync(c => c.Id == request.ReportingCurrencyId, ct);
        if (reportingCurrency is null)
            throw new BusinessRuleException("ReportingCurrencyId must reference an existing commodity.");
        if (reportingCurrency.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("ReportingCurrencyId must reference a currency commodity.");

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            AccountType = request.Kind.ToAccountType(),
            Kind = request.Kind,
            ReportingCurrencyId = request.ReportingCurrencyId,
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

        if (request.InitialBalance is { Amount: > 0 } initial)
        {
            await openingBalances.CreateAsync(
                account.Id, account.ReportingCurrencyId, initial.Amount,
                valuePerUnit: null, initial.AsOfDate, ct);
        }

        await db.SaveChangesAsync(ct);

        return ToResponse(account);
    }

    private static AccountResponse ToResponse(Account a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        AccountType = a.AccountType,
        Kind = a.Kind,
        ReportingCurrencyId = a.ReportingCurrencyId,
        Institution = a.Institution,
        IsSystem = a.IsSystem,
        IsArchived = a.IsArchived,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };
}
