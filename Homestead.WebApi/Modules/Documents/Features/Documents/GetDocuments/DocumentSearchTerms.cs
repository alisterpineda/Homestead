using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

/// <summary>
/// One search query's match branches — name, content, tag name, party name, notes — each written once as an EF
/// predicate, so the list filter (<see cref="Any"/>), the relevance tier (<see cref="Direct"/>), the rank and the
/// per-row "matched on" report cannot drift apart. Accents are folded the way the stored search vector is folded
/// (<c>unaccent</c>): the search text once, in the database, through <see cref="BuildAsync"/> <em>before</em> any
/// escaping, and the stored columns in each predicate — so "factură" and "Factura" meet, and a folded character can
/// never turn into live LIKE, tsquery or regex syntax after the escaper has run. The universal-search palette and
/// the list page both build their terms here.
/// </summary>
internal sealed class DocumentSearchTerms
{
    public const string TsLanguage = "english";

    /// <summary>Terms shorter than this match a whole word only under <see cref="DocumentMatchMode.Words"/>.</summary>
    public const int WordsPrefixMinLength = 4;

    /// <summary>Search text beyond this is cut (code points), bounding every predicate the text turns into.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>Under <see cref="DocumentMatchMode.Words"/>, terms past this many are dropped (one predicate each).</summary>
    public const int MaxTerms = 12;

    // Singleline, so Npgsql emits the pattern as is: without it the regex runs in Postgres's (?p) mode, where `.`
    // stops at a newline and a lookahead searched one line only.
    private const RegexOptions WordRegexOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    // The stored vector's weights (migration AddSearchVectorTriggers): name A, latest text B, summary C. A weighted
    // tsquery asks the vector which field matched, so the per-row report needs no re-tokenizing.
    private const string NameWeights = "A";
    private const string ContentWeights = "BC";

    private DocumentSearchTerms(
        DocumentMatchMode mode, string? tsQuery,
        Expression<Func<Document, bool>> name, Expression<Func<Document, bool>> content,
        Expression<Func<Document, bool>> tag, Expression<Func<Document, bool>> party,
        Expression<Func<Document, bool>> notes, Expression<Func<Document, float>> rank,
        Expression<Func<Document, bool>> nameReport, Expression<Func<Document, bool>> contentReport)
    {
        Mode = mode;
        TsQuery = tsQuery;
        Name = name;
        Content = content;
        Tag = tag;
        Party = party;
        Notes = notes;
        Rank = rank;
        Any = PredicateBuilder.Or(name, content, tag, party, notes);
        Direct = PredicateBuilder.Or(name, tag, party);
        NameReport = PredicateBuilder.Or(name, nameReport);
        ContentReport = contentReport;
    }

    public DocumentMatchMode Mode { get; }

    /// <summary>The tsquery text, built from the folded search; null when the query has no FTS terms.</summary>
    public string? TsQuery { get; }

    public Expression<Func<Document, bool>> Name { get; }
    public Expression<Func<Document, bool>> Content { get; }
    public Expression<Func<Document, bool>> Tag { get; }
    public Expression<Func<Document, bool>> Party { get; }
    public Expression<Func<Document, bool>> Notes { get; }

    /// <summary>Any branch matched: the search filter.</summary>
    public Expression<Func<Document, bool>> Any { get; }

    /// <summary>Matched by name, tag or party — what the document <em>is</em>, above what its text happens to mention.</summary>
    public Expression<Func<Document, bool>> Direct { get; }

    /// <summary>The full-text cover-density rank (0 without a vector or an FTS query).</summary>
    public Expression<Func<Document, float>> Rank { get; }

    /// <summary>
    /// For the per-row "matched on" report: <see cref="Content"/> goes through the stored vector, which also indexes
    /// the name and summary, so it cannot say which part matched. These ask the vector by weight — name lexemes (A)
    /// and text/summary lexemes (B, C) — so "content" means the text (or summary), and a name that matched only
    /// through stemming ("taxes" → "Tax") still reports "name".
    /// </summary>
    public Expression<Func<Document, bool>> NameReport { get; }

    public Expression<Func<Document, bool>> ContentReport { get; }

    /// <summary>
    /// The terms for <paramref name="search"/>: cut to <see cref="MaxSearchLength"/>, folded once in the database,
    /// then built. The one entry point the surfaces use, so no caller can build from unfolded text.
    /// </summary>
    public static async Task<DocumentSearchTerms> BuildAsync(
        DbContext db, string search, DocumentMatchMode mode, CancellationToken ct)
    {
        var raw = Truncate(search);
        var folded = await db.Database.SqlQuery<string>($"SELECT unaccent({raw}) AS \"Value\"").SingleAsync(ct);
        return Build(folded, mode, raw);
    }

    /// <summary>
    /// Builds from already-folded text. <paramref name="raw"/> is the unfolded text, used only by the substring
    /// branch over the extracted text, which matches the stored text as is so the trigram index serves it (accented
    /// content is still found through the folded vector).
    /// </summary>
    public static DocumentSearchTerms Build(string folded, DocumentMatchMode mode, string? raw = null) => mode switch
    {
        DocumentMatchMode.Words => BuildWords(folded),
        _ => BuildSubstring(folded, raw ?? folded),
    };

    /// <summary>The first <see cref="MaxSearchLength"/> code points of <paramref name="search"/>.</summary>
    public static string Truncate(string search)
    {
        if (search.Length <= MaxSearchLength)
            return search;

        var cut = MaxSearchLength;
        if (char.IsHighSurrogate(search[cut - 1]))
            cut--;
        return search[..cut];
    }

    // The list page's five OR'd modes (search.md › Query mechanics): substring on name, any version's text, tag and
    // party names, and the prefix-expanded FTS match. Notes are not searched here.
    private static DocumentSearchTerms BuildSubstring(string folded, string raw)
    {
        var tsQuery = SearchTsQueryBuilder.Build(folded);
        var pattern = LikePattern(folded);
        var rawPattern = LikePattern(raw);
        var hasTs = tsQuery is not null;
        var tsq = tsQuery ?? "";

        Expression<Func<Document, bool>> textSubstring = d => d.Versions.Any(v => v.ExtractedText != null
            && EF.Functions.ILike(v.ExtractedText, rawPattern, LikeEscaper.EscapeChar));

        return new DocumentSearchTerms(
            DocumentMatchMode.Substring,
            tsQuery,
            name: d => EF.Functions.ILike(EF.Functions.Unaccent(d.Name), pattern, LikeEscaper.EscapeChar),
            content: PredicateBuilder.Or(textSubstring, d => hasTs && d.SearchVector != null
                && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, tsq))),
            tag: d => d.DocumentTags.Any(dt => EF.Functions.ILike(EF.Functions.Unaccent(dt.Tag.Name), pattern, LikeEscaper.EscapeChar)),
            party: d => d.DocumentParties.Any(dp => EF.Functions.ILike(EF.Functions.Unaccent(dp.Party.Name), pattern, LikeEscaper.EscapeChar)),
            notes: _ => false,
            rank: RankOf(tsQuery),
            nameReport: VectorMatch(SearchTsQueryBuilder.Build(folded, weights: NameWeights)),
            contentReport: PredicateBuilder.Or(textSubstring, VectorMatch(SearchTsQueryBuilder.Build(folded, weights: ContentWeights))));
    }

    // Whole words: every term must appear as a word (a term of WordsPrefixMinLength+ characters: as the start of a
    // word) in the name, a tag name, a party name or the notes — one predicate per term, each a linear scan; content
    // goes through the full-text vector with the same short-term rule, so "car" never reaches "care", "card" or
    // "carrying" through prefix expansion.
    private static DocumentSearchTerms BuildWords(string folded)
    {
        var tsQuery = SearchTsQueryBuilder.Build(folded, WordsPrefixMinLength);
        var patterns = WordPatterns(folded, WordsPrefixMinLength);

        return new DocumentSearchTerms(
            DocumentMatchMode.Words,
            tsQuery,
            name: AllWords<Document>(d => d.Name, patterns),
            content: VectorMatch(tsQuery),
            tag: PredicateBuilder.Any<Document, DocumentTag>(d => d.DocumentTags, AllWords<DocumentTag>(dt => dt.Tag.Name, patterns)),
            party: PredicateBuilder.Any<Document, DocumentParty>(d => d.DocumentParties, AllWords<DocumentParty>(dp => dp.Party.Name, patterns)),
            notes: AllWords<Document>(d => d.Notes, patterns),
            rank: RankOf(tsQuery),
            nameReport: VectorMatch(SearchTsQueryBuilder.Build(folded, WordsPrefixMinLength, NameWeights)),
            contentReport: VectorMatch(SearchTsQueryBuilder.Build(folded, WordsPrefixMinLength, ContentWeights)));
    }

    private static string LikePattern(string search) => $"%{LikeEscaper.Escape(search.Replace("\"", string.Empty))}%";

    private static Expression<Func<Document, bool>> VectorMatch(string? tsQuery)
    {
        if (tsQuery is null)
            return _ => false;

        return d => d.SearchVector != null && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, tsQuery));
    }

    private static Expression<Func<Document, float>> RankOf(string? tsQuery)
    {
        if (tsQuery is null)
            return _ => 0f;

        return d => d.SearchVector != null
            ? d.SearchVector.RankCoverDensity(EF.Functions.ToTsQuery(TsLanguage, tsQuery))
            : 0f;
    }

    // Every pattern must match the folded text: one `unaccent(text) ~* pattern` per term, AND'd.
    private static Expression<Func<T, bool>> AllWords<T>(Expression<Func<T, string?>> text, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
            return _ => false;

        return PredicateBuilder.And(patterns
            .Select(pattern => PredicateBuilder.Compose(text,
                s => Regex.IsMatch(EF.Functions.Unaccent(s!), pattern, WordRegexOptions)))
            .ToArray());
    }

    /// <summary>
    /// One Postgres (ARE) regex per term of <paramref name="search"/>, each requiring the term as a word: <c>\m</c>
    /// starts a word, <c>\M</c> ends one. A term is trimmed to its word characters first (<c>#702-1234567</c> →
    /// <c>702-1234567</c>, <c>$120</c> → <c>120</c>; <c>&amp;</c> alone is dropped), because a boundary can never
    /// match next to punctuation. Quotes are dropped (adjacency is the full-text branch's affordance); regex
    /// metacharacters are escaped, which is safe only because the text is already folded. At most
    /// <see cref="MaxTerms"/> terms; empty when no term remains.
    /// </summary>
    public static IReadOnlyList<string> WordPatterns(string search, int prefixMinLength) =>
        search.Replace('"', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TrimToWordCharacters)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .Select(t => t.Length >= prefixMinLength
                ? $@"\m{Regex.Escape(t)}"
                : $@"\m{Regex.Escape(t)}\M")
            .ToList();

    // Postgres's \m and \M see a word as [[:alnum:]_].
    private static string TrimToWordCharacters(string term)
    {
        var start = 0;
        var end = term.Length;
        while (start < end && !IsWordCharacter(term[start]))
            start++;
        while (end > start && !IsWordCharacter(term[end - 1]))
            end--;
        return term[start..end];
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';
}
