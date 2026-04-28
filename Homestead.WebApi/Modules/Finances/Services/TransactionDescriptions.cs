namespace Homestead.WebApi.Modules.Finances.Services;

internal static class TransactionDescriptions
{
    public static string NormalizeOrDefault(string? description, string fallback)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }
}
