using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassificationScopeImageGateTests
{
    // Name / Parties / Summary / the dates benefit from the page image -> attach. Tags-only and None skip it.
    [Theory]
    [InlineData(ClassificationScope.Name, true)]
    [InlineData(ClassificationScope.Parties, true)]
    [InlineData(ClassificationScope.Summary, true)]
    [InlineData(ClassificationScope.DocumentDate, true)]
    [InlineData(ClassificationScope.ExpiryDate, true)]
    [InlineData(ClassificationScope.Tags | ClassificationScope.DocumentDate, true)]
    [InlineData(ClassificationScope.Tags, false)]
    [InlineData(ClassificationScope.None, false)]
    [InlineData(ClassificationScope.All, true)]
    [InlineData(ClassificationScope.Tags | ClassificationScope.Name, true)]
    [InlineData(ClassificationScope.Tags | ClassificationScope.Parties, true)]
    [InlineData(ClassificationScope.Name | ClassificationScope.Summary, true)]
    [InlineData(ClassificationScope.Tags | ClassificationScope.Summary, true)]
    public void ShouldAttachImage_MatchesTruthTable(ClassificationScope scope, bool expected)
    {
        Assert.Equal(expected, scope.ShouldAttachImage());
    }
}
