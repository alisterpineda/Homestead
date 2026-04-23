using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public record InitialBalanceInput
{
    public required decimal Amount { get; init; }
    public required DateOnly AsOfDate { get; init; }
}

public record CreateAccountCommand : IRequest<AccountResponse>
{
    public required string Name { get; init; }
    public required AccountKind Kind { get; init; }
    public required Guid ReportingCurrencyId { get; init; }
    public string? Institution { get; init; }
    public InitialBalanceInput? InitialBalance { get; init; }
}
