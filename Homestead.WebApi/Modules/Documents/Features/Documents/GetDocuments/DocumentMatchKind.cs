namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

/// <summary>
/// Where a search matched a document, as reported in <see cref="GetDocumentsResponse.MatchedOn"/>. <see cref="Order"/>
/// is the report's order and the one list every surface (the MCP tools' <c>matched_on</c>, the docs) restates.
/// </summary>
public static class DocumentMatchKind
{
    public const string Name = "name";

    /// <summary>The latest version's text or the summary.</summary>
    public const string Content = "content";

    public const string Tag = "tag";
    public const string Party = "party";
    public const string Notes = "notes";

    public static readonly string[] Order = [Name, Content, Tag, Party, Notes];
}
