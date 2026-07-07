namespace Homestead.WebApi.Common.Search;

/// <summary>
/// Escapes user input for use inside a <c>LIKE</c>/<c>ILIKE</c> pattern so its wildcard metacharacters
/// (<c>%</c>, <c>_</c>) are matched literally.
/// </summary>
/// <remarks>
/// Npgsql's 2-arg <c>EF.Functions.ILike(col, pattern)</c> emits <c>ILIKE … ESCAPE ''</c>, which
/// <em>disables</em> escaping entirely — so backslash-escaping alone does nothing. Pair this with the
/// 3-arg overload <c>EF.Functions.ILike(col, pattern, LikeEscaper.EscapeChar)</c> so Postgres actually
/// honours the backslash escape and a user-supplied <c>%</c>/<c>_</c> is treated as a literal.
/// </remarks>
public static class LikeEscaper
{
    /// <summary>The escape character to pass to the 3-arg <c>EF.Functions.ILike</c> overload.</summary>
    public const string EscapeChar = "\\";

    /// <summary>
    /// Escapes <c>\</c>, <c>%</c>, and <c>_</c> so they match literally. Backslash is escaped first so
    /// the escape characters introduced for <c>%</c>/<c>_</c> are not themselves re-escaped.
    /// </summary>
    public static string Escape(string input) =>
        input
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
}
