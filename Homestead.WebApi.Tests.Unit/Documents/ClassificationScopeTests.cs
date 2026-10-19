using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassificationScopeTests
{
    // Spelled out on purpose: the production default is derived (All minus Summary), so this pins its value.
    private const ClassificationScope DefaultScope =
        ClassificationScope.Tags | ClassificationScope.Parties | ClassificationScope.Name
        | ClassificationScope.DocumentDate | ClassificationScope.ExpiryDate;

    [Fact]
    public void FromFields_Null_DefaultsToClassifyFields_IncludingDates_NeverSummary()
    {
        var scope = ClassificationScopeExtensions.FromFields(null);

        Assert.Equal(DefaultScope, scope);
        Assert.False(scope.HasFlag(ClassificationScope.Summary));
    }

    [Fact]
    public void FromFields_Empty_DefaultsToClassifyFields_IncludingDates_NeverSummary()
    {
        var scope = ClassificationScopeExtensions.FromFields([]);

        Assert.Equal(DefaultScope, scope);
        Assert.False(scope.HasFlag(ClassificationScope.Summary));
    }

    [Theory]
    [InlineData(DocumentSuggestionField.Tags, ClassificationScope.Tags)]
    [InlineData(DocumentSuggestionField.Parties, ClassificationScope.Parties)]
    [InlineData(DocumentSuggestionField.Name, ClassificationScope.Name)]
    [InlineData(DocumentSuggestionField.DocumentDate, ClassificationScope.DocumentDate)]
    [InlineData(DocumentSuggestionField.ExpiryDate, ClassificationScope.ExpiryDate)]
    public void FromFields_MapsEachFieldToItsFlag(DocumentSuggestionField field, ClassificationScope expected)
    {
        Assert.Equal(expected, ClassificationScopeExtensions.FromFields([field]));
    }

    [Fact]
    public void FromFields_CombinesFields()
    {
        var scope = ClassificationScopeExtensions.FromFields(
            [DocumentSuggestionField.DocumentDate, DocumentSuggestionField.ExpiryDate]);

        Assert.Equal(ClassificationScope.DocumentDate | ClassificationScope.ExpiryDate, scope);
    }
}
