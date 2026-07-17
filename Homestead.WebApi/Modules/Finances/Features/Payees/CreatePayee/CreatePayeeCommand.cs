using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;

public record CreatePayeeCommand : ICommand<PayeeResponse>
{
    public required string Name { get; init; }

    /// <summary>Optional default category (Income/Expense) the flow sheet autofills client-side. Never applied server-side.</summary>
    public Guid? DefaultCategoryId { get; init; }

    public string? Notes { get; init; }
}
