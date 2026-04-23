using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Common;

public static class JsonPatchExtensions
{
    public static void ApplyOrThrow<T>(this JsonPatchDocument<T> patch, T target) where T : class
    {
        var errors = new List<JsonPatchError>();
        patch.ApplyTo(target, error => errors.Add(error));
        if (errors.Count == 0)
            return;

        throw new BusinessRuleException(errors.Select(e => new
        {
            Operation = e.Operation?.ToString(),
            e.ErrorMessage
        }));
    }
}
