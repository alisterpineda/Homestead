using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public record CreateAccountCommand : IRequest<AccountResponse>
{
    public required string Name { get; init; }
    public required AccountType AccountType { get; init; }
    public required Guid DefaultCommodityId { get; init; }
    public Guid? ParentId { get; init; }
    public bool IsMultiPosition { get; init; }
    public string? Institution { get; init; }
}
