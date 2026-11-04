namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

/// <summary>
/// How a search query matches text. <see cref="Substring"/> is the list page's: a case-insensitive substring of the
/// name, any version's text, a tag name or a party name, plus the prefix-expanded full-text match — forgiving of
/// partial typing. <see cref="Words"/> matches whole words (a term of four or more characters also as a word prefix)
/// in the name, tag names, party names and notes, and the full-text vector for content, with short terms exact — so
/// "car" is not "care", "card" or "carrying". Both fold accents.
/// </summary>
public enum DocumentMatchMode
{
    Substring,
    Words
}
