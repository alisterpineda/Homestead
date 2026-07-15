using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public record InitialBalanceInput
{
    public required decimal Amount { get; init; }
    public required DateOnly AsOfDate { get; init; }
}

public record CreateAccountCommand : ICommand<AccountResponse>
{
    public required string Name { get; init; }
    public required AccountKind Kind { get; init; }
    public required Guid ReportingCurrencyId { get; init; }
    public string? Institution { get; init; }
    public string? AccountNumber { get; init; }
    public InitialBalanceInput? InitialBalance { get; init; }
}
