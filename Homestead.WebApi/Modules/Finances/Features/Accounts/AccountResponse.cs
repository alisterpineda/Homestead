using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts;

public record AccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AccountType AccountType { get; init; }
    public Guid? ParentId { get; init; }
    public required Guid DefaultCommodityId { get; init; }
    public required bool IsMultiPosition { get; init; }
    public string? Institution { get; init; }
    public required bool IsSystem { get; init; }
    public required bool IsArchived { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
