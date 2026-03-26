using Homestead.WebApi.Features.Documents.Export;

namespace Homestead.WebApi.Tests.Unit.Documents.Export;

public class TagBasedExportPathBuilderTests
{
    private readonly TagBasedExportPathBuilder _sut = new();

    [Fact]
    public void UntaggedDocument_GoesToUntaggedFolder()
    {
        var metadata = new ExportDocumentMetadata("Invoice", ".pdf", [], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Single(paths);
        Assert.Equal(Path.Combine("_Untagged", "Invoice.pdf"), paths[0]);
    }

    [Fact]
    public void SingleTag_ReturnsSinglePath()
    {
        var metadata = new ExportDocumentMetadata("Policy", ".pdf", ["Insurance/Home"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Single(paths);
        Assert.Equal(Path.Combine("Insurance", "Home", "Policy.pdf"), paths[0]);
    }

    [Fact]
    public void MultipleTags_ReturnsDuplicatedPaths()
    {
        var metadata = new ExportDocumentMetadata("W2", ".pdf", ["Tax/2024", "Insurance"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Equal(2, paths.Count);
        Assert.Equal(Path.Combine("Tax", "2024", "W2.pdf"), paths[0]);
        Assert.Equal(Path.Combine("Insurance", "W2.pdf"), paths[1]);
    }

    [Fact]
    public void WithParties_IncludesPartiesInFileName()
    {
        var metadata = new ExportDocumentMetadata("Policy", ".pdf", ["Insurance"], ["State Farm", "Allstate"]);

        var paths = _sut.BuildPaths(metadata);

        Assert.Single(paths);
        Assert.Equal(Path.Combine("Insurance", "Policy - State Farm, Allstate.pdf"), paths[0]);
    }

    [Fact]
    public void WithSingleParty_IncludesPartyInFileName()
    {
        var metadata = new ExportDocumentMetadata("W2", ".pdf", ["Tax"], ["Employer Inc"]);

        var paths = _sut.BuildPaths(metadata);

        Assert.Equal(Path.Combine("Tax", "W2 - Employer Inc.pdf"), paths[0]);
    }

    [Fact]
    public void EmptyPartyList_ExcludesPartySuffix()
    {
        var metadata = new ExportDocumentMetadata("W2", ".pdf", ["Tax"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Equal(Path.Combine("Tax", "W2.pdf"), paths[0]);
    }

    [Fact]
    public void IllegalCharactersInDocumentName_AreSanitized()
    {
        var metadata = new ExportDocumentMetadata("file:name*test?", ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.DoesNotContain(":", paths[0]);
        Assert.DoesNotContain("*", paths[0]);
        Assert.DoesNotContain("?", paths[0]);
    }

    [Fact]
    public void IllegalCharactersInTagPath_AreSanitized()
    {
        var metadata = new ExportDocumentMetadata("Doc", ".pdf", ["Tax:2024/W2*Forms"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.DoesNotContain(":", paths[0]);
        Assert.DoesNotContain("*", paths[0]);
    }

    [Fact]
    public void EmptyDocumentName_UsesFallback()
    {
        var metadata = new ExportDocumentMetadata("", ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Contains("Untitled.pdf", paths[0]);
    }

    [Fact]
    public void DocumentNameWithOnlyIllegalChars_SanitizesToUnderscores()
    {
        var metadata = new ExportDocumentMetadata("???", ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        // Each ? becomes _, so the name is "___"
        var fileName = Path.GetFileName(paths[0]);
        Assert.DoesNotContain("?", fileName);
        Assert.EndsWith(".pdf", fileName);
    }

    [Fact]
    public void VeryLongDocumentName_IsTruncated()
    {
        var longName = new string('A', 300);
        var metadata = new ExportDocumentMetadata(longName, ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        var fileName = Path.GetFileName(paths[0]);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        Assert.True(nameWithoutExtension.Length <= 200);
        Assert.EndsWith(".pdf", fileName);
    }

    [Fact]
    public void EmptyTagPathSegments_UseFallback()
    {
        var metadata = new ExportDocumentMetadata("Doc", ".pdf", ["Insurance//Home"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Contains("Unknown Tag", paths[0]);
    }

    [Fact]
    public void UntaggedWithParties_CombinesCorrectly()
    {
        var metadata = new ExportDocumentMetadata("Contract", ".pdf", [], ["Acme Corp"]);

        var paths = _sut.BuildPaths(metadata);

        Assert.Single(paths);
        Assert.Equal(Path.Combine("_Untagged", "Contract - Acme Corp.pdf"), paths[0]);
    }

    [Fact]
    public void PartyWithIllegalChars_SanitizesPartyName()
    {
        var metadata = new ExportDocumentMetadata("Doc", ".pdf", ["Tag"], ["Company: Ltd"]);

        var paths = _sut.BuildPaths(metadata);

        Assert.DoesNotContain(":", Path.GetFileName(paths[0]));
    }

    [Fact]
    public void ControlCharactersInName_AreReplaced()
    {
        var metadata = new ExportDocumentMetadata("test\x00name\x1F", ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        var fileName = Path.GetFileName(paths[0]);
        // Control chars are replaced with _, so filename should contain "test_name_"
        Assert.Contains("test_name", fileName);
    }

    [Fact]
    public void LeadingTrailingDots_AreTrimmedFromName()
    {
        var metadata = new ExportDocumentMetadata("...test file...", ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        var fileName = Path.GetFileNameWithoutExtension(Path.GetFileName(paths[0]));
        Assert.False(fileName.StartsWith('.'));
        Assert.False(fileName.EndsWith('.'));
    }

    [Fact]
    public void ConsecutiveWhitespace_IsCollapsedInName()
    {
        var metadata = new ExportDocumentMetadata("hello   world", ".pdf", ["Tag"], []);

        var paths = _sut.BuildPaths(metadata);

        Assert.Contains("hello world", paths[0]);
        Assert.DoesNotContain("   ", paths[0]);
    }
}
