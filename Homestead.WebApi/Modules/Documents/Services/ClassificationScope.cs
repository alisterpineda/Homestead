using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Services;

/// <summary>
/// The set of document fields an AI classification run should generate and apply.
/// Field-scoped classification lets the user refresh one field (e.g. Tags) without
/// re-generating or overwriting the others, and avoids spending tokens on discarded output.
/// </summary>
[Flags]
public enum ClassificationScope
{
    None = 0,
    Tags = 1,
    Parties = 2,
    Name = 4,
    Summary = 8,
    DocumentDate = 16,
    ExpiryDate = 32,
    All = Tags | Parties | Name | Summary | DocumentDate | ExpiryDate
}

public static class ClassificationScopeExtensions
{
    /// <summary>
    /// Maps the API-facing <see cref="DocumentSuggestionField"/> set to an internal
    /// <see cref="ClassificationScope"/>. A null or empty set defaults to every classify field
    /// (<see cref="ClassificationScope.All"/> minus Summary) and <b>never</b> includes Summary —
    /// Summary is only ever requested explicitly via the separate summarize path.
    /// </summary>
    public static ClassificationScope FromFields(IReadOnlyCollection<DocumentSuggestionField>? fields)
    {
        if (fields is null || fields.Count == 0)
            return ClassificationScope.All & ~ClassificationScope.Summary;

        var scope = ClassificationScope.None;
        foreach (var field in fields)
        {
            scope |= field switch
            {
                DocumentSuggestionField.Tags => ClassificationScope.Tags,
                DocumentSuggestionField.Parties => ClassificationScope.Parties,
                DocumentSuggestionField.Name => ClassificationScope.Name,
                DocumentSuggestionField.DocumentDate => ClassificationScope.DocumentDate,
                DocumentSuggestionField.ExpiryDate => ClassificationScope.ExpiryDate,
                _ => ClassificationScope.None
            };
        }

        return scope;
    }

    /// <summary>
    /// Whether a classification run for this scope should attach the page-1 image. The visual cues an image
    /// adds (letterhead, title block, layout) most help <see cref="ClassificationScope.Name"/>,
    /// <see cref="ClassificationScope.Parties"/>, and <see cref="ClassificationScope.Summary"/>. The date
    /// fields (<see cref="ClassificationScope.DocumentDate"/>, <see cref="ClassificationScope.ExpiryDate"/>)
    /// want it too: dates sit in headers and footers that OCR garbles, and the PDF covers every page.
    /// Tags-only (and None) skip it: the text + tag catalog dominate tag selection, and skipping spares the
    /// ~30s vision prefill.
    /// </summary>
    public static bool ShouldAttachImage(this ClassificationScope scope) =>
        scope.HasFlag(ClassificationScope.Name)
        || scope.HasFlag(ClassificationScope.Parties)
        || scope.HasFlag(ClassificationScope.Summary)
        || scope.HasFlag(ClassificationScope.DocumentDate)
        || scope.HasFlag(ClassificationScope.ExpiryDate);
}
