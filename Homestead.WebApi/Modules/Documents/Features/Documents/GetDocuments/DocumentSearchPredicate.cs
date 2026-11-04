using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

/// <summary>
/// The document list's text-search predicate, shared so the document list page and the universal-search palette
/// can never silently diverge on what "matches" a query. Both surfaces build the same <see cref="DocumentSearchTerms"/>
/// and filter with its <see cref="DocumentSearchTerms.Any"/>.
/// </summary>
internal static class DocumentSearchPredicate
{
    public const string TsLanguage = DocumentSearchTerms.TsLanguage;

    /// <summary>The list page's five OR'd substring/FTS modes (<see cref="DocumentMatchMode.Substring"/>).</summary>
    public static IQueryable<Document> ApplyTextSearch(IQueryable<Document> query, string search) =>
        query.Where(DocumentSearchTerms.Build(search, DocumentMatchMode.Substring).Any);
}
