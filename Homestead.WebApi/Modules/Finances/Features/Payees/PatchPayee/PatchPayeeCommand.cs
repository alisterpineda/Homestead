using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.PatchPayee;

/// <summary>
/// Full-replace edit of a payee (PatchCategory precedent — the whole editable body is sent, not a
/// JSON-patch document). <see cref="Id"/> is bound from the route.
/// </summary>
public record PatchPayeeCommand : ICommand<PayeeResponse>
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? DefaultCategoryId { get; init; }
    public string? Notes { get; init; }
    public bool IsArchived { get; init; }
}
