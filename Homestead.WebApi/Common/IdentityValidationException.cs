namespace Homestead.WebApi.Common;

public class IdentityValidationException(IDictionary<string, string[]> errors) : Exception
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
