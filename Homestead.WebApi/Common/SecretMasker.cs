namespace Homestead.WebApi.Common;

public static class SecretMasker
{
    public static string? Mask(string? value) =>
        string.IsNullOrEmpty(value) ? value : value.Length >= 5 ? "••••" + value[^4..] : "••••";
}
