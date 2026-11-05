namespace Homestead.WebApi.Modules.Mcp;

// Closest existing names for a name the model sent that does not exist: a trigram "did you mean" in the spirit of
// search_documents's (pg_trgm), done in C# because the candidate sets here — a household's tag paths or party
// names — are small enough to load whole on the miss path, and the unit tests run on the in-memory provider, which
// has no pg_trgm. Trigrams are built as pg_trgm builds them (lowercased words, two leading and one trailing space of
// padding), but the score is query-trigram coverage — the share of the query's trigrams the candidate contains — so a
// short query against a long name ("TD Bank" → "TD Canada Trust") scores on what it shares rather than being diluted
// by what it lacks. It is not pg_trgm's word_similarity and the threshold is its own (the list page's, in
// GetDocumentsHandler, is tuned separately).
public static class McpNearest
{
    public const double Threshold = 0.3;
    public const int Max = 3;

    // Up to Max candidates scoring at least Threshold against unknown, best first (then shortest, then alphabetical).
    public static string[] Find(string unknown, IEnumerable<string> candidates)
    {
        var query = Trigrams(unknown);
        if (query.Count == 0)
            return [];

        return candidates
            .Select(c => (Name: c, Score: Score(query, Trigrams(c))))
            .Where(x => x.Score >= Threshold)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Max)
            .Select(x => x.Name)
            .ToArray();
    }

    public static double Score(string query, string candidate) => Score(Trigrams(query), Trigrams(candidate));

    private static double Score(HashSet<string> query, HashSet<string> candidate) =>
        query.Count == 0 ? 0 : (double)query.Count(candidate.Contains) / query.Count;

    private static HashSet<string> Trigrams(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var words = text.ToLowerInvariant()
            .Split(c => !char.IsLetterOrDigit(c));
        foreach (var word in words)
        {
            var padded = "  " + word + " ";
            for (var i = 0; i + 3 <= padded.Length; i++)
                set.Add(padded.Substring(i, 3));
        }

        return set;
    }

    private static IEnumerable<string> Split(this string text, Func<char, bool> isSeparator)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var separator = i == text.Length || isSeparator(text[i]);
            if (separator && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
            else if (!separator && start < 0)
            {
                start = i;
            }
        }
    }

    // "Unknown tag path(s): A, B — nearest: X, Y" (one unknown) or "… — nearest: A → X, Y; B → Z" (several), for the
    // unknowns that have any nearest.
    public static string Describe(
        string what, IReadOnlyList<string> unknown, IReadOnlyList<(string Unknown, string[] Nearest)> suggestions)
    {
        var message = $"Unknown {what}: " + string.Join(", ", unknown);
        if (suggestions.Count > 0)
        {
            message += " — nearest: " + (unknown.Count == 1
                ? string.Join(", ", suggestions[0].Nearest)
                : string.Join("; ", suggestions.Select(s => $"{s.Unknown} → {string.Join(", ", s.Nearest)}")));
        }

        return message;
    }
}
