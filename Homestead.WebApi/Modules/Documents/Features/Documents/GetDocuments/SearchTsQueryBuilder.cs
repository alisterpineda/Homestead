using System.Text.RegularExpressions;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

internal static class SearchTsQueryBuilder
{
    private static readonly Regex QuotedPhraseRegex = new("\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly HashSet<string> PhraseStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "and", "or", "to", "in", "on", "at", "for"
    };

    /// <summary>
    /// The tsquery for <paramref name="search"/>: quoted phrases as adjacency chains, every other term prefix-expanded
    /// (<c>tax:*</c>) — except terms shorter than <paramref name="prefixMinLength"/>, which match their lexeme exactly
    /// (so under a minimum of 4, <c>car</c> is <c>car</c>, not <c>care</c>, <c>card</c> or <c>carri</c>). Null when
    /// nothing remains.
    /// </summary>
    public static string? Build(string search, int prefixMinLength = 1)
    {
        var clauses = new List<string>();

        var remainder = QuotedPhraseRegex.Replace(search, match =>
        {
            var inner = match.Groups[1].Value;
            var phraseClause = BuildPhraseClause(inner);
            if (phraseClause is not null)
            {
                clauses.Add(phraseClause);
            }
            return " ";
        });

        remainder = remainder.Replace('"', ' ');

        var prefixTerms = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeTsQueryTerm)
            .Where(t => t.Length > 0)
            .Select(t => t.Length >= prefixMinLength ? t + ":*" : t);

        clauses.AddRange(prefixTerms);

        return clauses.Count > 0 ? string.Join(" & ", clauses) : null;
    }

    private static string? BuildPhraseClause(string phrase)
    {
        var tokens = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeTsQueryTerm)
            .Where(t => t.Length > 0 && !PhraseStopwords.Contains(t))
            .ToList();

        return tokens.Count switch
        {
            0 => null,
            1 => tokens[0],
            _ => "(" + string.Join(" <-> ", tokens) + ")"
        };
    }

    private static string SanitizeTsQueryTerm(string term)
    {
        var chars = term.Where(c => c != '&' && c != '|' && c != '!' && c != '('
            && c != ')' && c != ':' && c != '*' && c != '\'' && c != '\\'
            && c != '"' && c != '<' && c != '>').ToArray();
        return new string(chars);
    }
}
