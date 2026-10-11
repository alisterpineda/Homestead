namespace Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;

public class PatchAccountModel
{
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string? AccountNumber { get; set; }
    public bool IsArchived { get; set; }
}
