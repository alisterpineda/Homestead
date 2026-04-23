using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface ITransactionValidator
{
    void Validate(AccountTransaction transaction);
}
