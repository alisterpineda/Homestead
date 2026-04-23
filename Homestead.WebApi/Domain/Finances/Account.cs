using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class Account
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AccountType AccountType { get; set; }
    public AccountKind? Kind { get; set; }

    public Guid ReportingCurrencyId { get; set; }
    public Commodity ReportingCurrency { get; set; } = null!;

    public string? Institution { get; set; }
    public bool IsSystem { get; set; }
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
