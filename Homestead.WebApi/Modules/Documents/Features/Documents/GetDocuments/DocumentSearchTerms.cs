using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

/// <summary>
/// One search query's match branches — name, content, tag name, party name, notes — each written once as an EF
/// predicate, so the list filter (<see cref="Any"/>), the relevance tier (<see cref="Direct"/>), the rank and the
/// per-row "matched on" report cannot drift apart. Accents are folded on both sides in SQL (<c>unaccent</c>, the
/// same folding the stored search vector gets), so "factură" and "Factura" meet. The universal-search palette and
/// the list page share this through <see cref="DocumentSearchPredicate"/>.
/// </summary>
internal sealed class DocumentSearchTerms
{
    public const string TsLanguage = "english";

    /// <summary>Terms shorter than this match a whole word only under <see cref="DocumentMatchMode.Words"/>.</summary>
    public const int WordsPrefixMinLength = 4;

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

    /// <summary>The tsquery text (unfolded; folded in SQL where it is used), null when the query has no FTS terms.</summary>
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
    /// For the per-row "matched on" report only (a page of rows, never the whole table): <see cref="Content"/>
    /// goes through the stored vector, which also indexes the name and summary — fast, but it cannot say which
    /// part matched. These re-derive the name's and the text/summary's own vectors on the fly, so "content" means
    /// the text (or summary) and a name that matched only through stemming ("taxes" → "Tax") still reports "name".
    /// </summary>
    public Expression<Func<Document, bool>> NameReport { get; }

    public Expression<Func<Document, bool>> ContentReport { get; }

    public static DocumentSearchTerms Build(string search, DocumentMatchMode mode) => mode switch
    {
        DocumentMatchMode.Words => BuildWords(search),
        _ => BuildSubstring(search),
    };

    // The list page's five OR'd modes (search.md › Query mechanics): substring on name, any version's text, tag and
    // party names, and the prefix-expanded FTS match. Notes are not searched here.
    private static DocumentSearchTerms BuildSubstring(string search)
    {
        var tsQuery = SearchTsQueryBuilder.Build(search);
        var pattern = $"%{LikeEscaper.Escape(search.Replace("\"", string.Empty))}%";
        var hasTs = tsQuery is not null;
        var tsq = tsQuery ?? "";

        return new DocumentSearchTerms(
            DocumentMatchMode.Substring,
            tsQuery,
            name: d => EF.Functions.ILike(EF.Functions.Unaccent(d.Name), EF.Functions.Unaccent(pattern), LikeEscaper.EscapeChar),
            content: d => d.Versions.Any(v => v.ExtractedText != null
                                              && EF.Functions.ILike(EF.Functions.Unaccent(v.ExtractedText), EF.Functions.Unaccent(pattern), LikeEscaper.EscapeChar))
                          || (hasTs && d.SearchVector != null
                              && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsq)))),
            tag: d => d.DocumentTags.Any(dt => EF.Functions.ILike(EF.Functions.Unaccent(dt.Tag.Name), EF.Functions.Unaccent(pattern), LikeEscaper.EscapeChar)),
            party: d => d.DocumentParties.Any(dp => EF.Functions.ILike(EF.Functions.Unaccent(dp.Party.Name), EF.Functions.Unaccent(pattern), LikeEscaper.EscapeChar)),
            notes: _ => false,
            rank: RankOf(tsQuery),
            nameReport: NameFts(tsQuery),
            contentReport: d => d.Versions.Any(v => v.ExtractedText != null
                                                    && EF.Functions.ILike(EF.Functions.Unaccent(v.ExtractedText), EF.Functions.Unaccent(pattern), LikeEscaper.EscapeChar))
                                || (hasTs && (EF.Functions.ToTsVector(TsLanguage, EF.Functions.Unaccent(
                                                  d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ExtractedText).FirstOrDefault() ?? ""))
                                              .Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsq)))
                                          || EF.Functions.ToTsVector(TsLanguage, EF.Functions.Unaccent(d.Summary ?? ""))
                                              .Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsq))))));
    }

    // Whole words: every term must appear as a word (a term of WordsPrefixMinLength+ characters: as the start of a
    // word) in the name, a tag name, a party name or the notes; content goes through the full-text vector with the
    // same short-term rule, so "car" never reaches "care", "card" or "carrying" through prefix expansion.
    private static DocumentSearchTerms BuildWords(string search)
    {
        var tsQuery = SearchTsQueryBuilder.Build(search, WordsPrefixMinLength);
        var pattern = WordPattern(search, WordsPrefixMinLength);
        var hasPattern = pattern is not null;
        var regex = pattern ?? "";
        var hasTs = tsQuery is not null;
        var tsq = tsQuery ?? "";

        return new DocumentSearchTerms(
            DocumentMatchMode.Words,
            tsQuery,
            name: d => hasPattern && Regex.IsMatch(EF.Functions.Unaccent(d.Name), EF.Functions.Unaccent(regex), RegexOptions.IgnoreCase),
            content: d => hasTs && d.SearchVector != null
                          && d.SearchVector.Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsq))),
            tag: d => hasPattern && d.DocumentTags.Any(dt => Regex.IsMatch(EF.Functions.Unaccent(dt.Tag.Name), EF.Functions.Unaccent(regex), RegexOptions.IgnoreCase)),
            party: d => hasPattern && d.DocumentParties.Any(dp => Regex.IsMatch(EF.Functions.Unaccent(dp.Party.Name), EF.Functions.Unaccent(regex), RegexOptions.IgnoreCase)),
            notes: d => hasPattern && Regex.IsMatch(EF.Functions.Unaccent(d.Notes), EF.Functions.Unaccent(regex), RegexOptions.IgnoreCase),
            rank: RankOf(tsQuery),
            nameReport: NameFts(tsQuery),
            contentReport: d => hasTs && (EF.Functions.ToTsVector(TsLanguage, EF.Functions.Unaccent(
                                                  d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.ExtractedText).FirstOrDefault() ?? ""))
                                              .Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsq)))
                                          || EF.Functions.ToTsVector(TsLanguage, EF.Functions.Unaccent(d.Summary ?? ""))
                                              .Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsq)))));
    }

    private static Expression<Func<Document, bool>> NameFts(string? tsQuery)
    {
        if (tsQuery is null)
            return _ => false;

        return d => EF.Functions.ToTsVector(TsLanguage, EF.Functions.Unaccent(d.Name))
            .Matches(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsQuery)));
    }

    private static Expression<Func<Document, float>> RankOf(string? tsQuery)
    {
        if (tsQuery is null)
            return _ => 0f;

        return d => d.SearchVector != null
            ? d.SearchVector.RankCoverDensity(EF.Functions.ToTsQuery(TsLanguage, EF.Functions.Unaccent(tsQuery)))
            : 0f;
    }

    /// <summary>
    /// A Postgres (ARE) regex requiring every term of <paramref name="search"/> as a word: <c>\m</c> starts a word,
    /// <c>\M</c> ends one, and one lookahead per term makes the order irrelevant. Quotes are dropped (adjacency is
    /// the full-text branch's affordance); regex metacharacters in a term are escaped. Null when no term remains.
    /// </summary>
    public static string? WordPattern(string search, int prefixMinLength)
    {
        var terms = search.Replace('"', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('\'', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}'))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count == 0)
            return null;

        return string.Concat(terms.Select(t =>
            t.Length >= prefixMinLength
                ? $@"(?=.*\m{Regex.Escape(t)})"
                : $@"(?=.*\m{Regex.Escape(t)}\M)"));
    }
}
