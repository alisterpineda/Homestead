using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Documents;

// Each DocumentSuggestionField has touch-points that fall through silently when a new field misses them
// (listed on the enum). These fail instead, for every current and future field.
public class SuggestionFieldCoverageTests
{
    public static TheoryData<DocumentSuggestionField> AllFields() => new(Enum.GetValues<DocumentSuggestionField>());

    [Theory]
    [MemberData(nameof(AllFields))]
    public void EveryField_MapsToAClassificationScopeFlag_InAll(DocumentSuggestionField field)
    {
        var scope = ClassificationScopeExtensions.FromFields([field]);

        Assert.NotEqual(ClassificationScope.None, scope);
        Assert.True(ClassificationScope.All.HasFlag(scope));
    }

    [Theory]
    [MemberData(nameof(AllFields))]
    public void EveryField_HasAPatchPathThatAutoDeclinesIt(DocumentSuggestionField field)
    {
        Assert.Contains(field, PatchDocumentHandler.SuggestionFieldsByPatchPath.Values);
    }

    [Theory]
    [MemberData(nameof(AllFields))]
    public void EveryField_HasAnExplicitRevert(DocumentSuggestionField field)
    {
        // The reverter throws on a field without its own case.
        var isLink = field is DocumentSuggestionField.Tags or DocumentSuggestionField.Parties;
        var suggestion = new DocumentSuggestion
        {
            Field = field,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = isLink ? Guid.NewGuid().ToString() : "2025-03-15",
            OriginalValue = "2025-01-01",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var exception = Record.Exception(() => DocumentSuggestionReverter.Revert(new Document { Name = "Doc" }, suggestion));

        Assert.Null(exception);
    }
}
