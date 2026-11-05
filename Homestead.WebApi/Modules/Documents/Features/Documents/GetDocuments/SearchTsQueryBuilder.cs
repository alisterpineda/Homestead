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
    /// (so under a minimum of 4, <c>car</c> is <c>car</c>, not <c>care</c>, <c>card</c> or <c>carri</c>). With
    /// <paramref name="weights"/> ("A", "BC"), every term is restricted to lexemes carrying one of those weights
    /// (<c>tax:*BC</c>), which asks the stored vector which field matched. Null when nothing remains. The search text
    /// must already be accent-folded: folding after sanitizing would let a folded character (<c>«</c> → <c>&lt;&lt;</c>,
    /// <c>¡</c> → <c>!</c>) regrow a tsquery operator.
    /// </summary>
    public static string? Build(string search, int prefixMinLength = 1, string? weights = null)
    {
        var clauses = new List<string>();

        var remainder = QuotedPhraseRegex.Replace(search, match =>
        {
            var inner = match.Groups[1].Value;
            var phraseClause = BuildPhraseClause(inner, weights);
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
            .Select(t => t.Length >= prefixMinLength ? t + ":*" + weights : Label(t, weights));

        clauses.AddRange(prefixTerms);

        return clauses.Count > 0 ? string.Join(" & ", clauses) : null;
    }

    private static string? BuildPhraseClause(string phrase, string? weights)
    {
        var tokens = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeTsQueryTerm)
            .Where(t => t.Length > 0 && !PhraseStopwords.Contains(t))
            .Select(t => Label(t, weights))
            .ToList();

        return tokens.Count switch
        {
            0 => null,
            1 => tokens[0],
            _ => "(" + string.Join(" <-> ", tokens) + ")"
        };
    }

    private static string Label(string term, string? weights) => weights is null ? term : term + ":" + weights;

    private static string SanitizeTsQueryTerm(string term)
    {
        var chars = term.Where(c => c != '&' && c != '|' && c != '!' && c != '('
            && c != ')' && c != ':' && c != '*' && c != '\'' && c != '\\'
            && c != '"' && c != '<' && c != '>').ToArray();
        return new string(chars);
    }
}
