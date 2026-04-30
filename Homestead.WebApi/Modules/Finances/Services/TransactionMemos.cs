namespace Homestead.WebApi.Modules.Finances.Services;

internal static class TransactionMemos
{
    public static string NormalizeOrDefault(string? memo, string fallback)
    {
        var trimmed = memo?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }
}
