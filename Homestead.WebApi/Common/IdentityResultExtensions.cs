using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Common;

public static class IdentityResultExtensions
{
    public static IDictionary<string, string[]> ToErrorDictionary(this IdentityResult result)
        => result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description });
}
