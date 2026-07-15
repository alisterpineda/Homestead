using Homestead.WebApi.Common;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;

public class PatchAccountModel
{
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string? AccountNumber { get; set; }
    public bool IsArchived { get; set; }
}

public record PatchAccountCommand : ICommand<AccountResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchAccountModel> PatchDocument { get; init; }
}
