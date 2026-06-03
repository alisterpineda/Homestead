using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface ITransactionValidator
{
    Task ValidateAsync(AccountTransaction transaction, CancellationToken ct);
}
