using Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;
using Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// Key-existence validation on the per-request override. There is no enum or closed set to check against
// — what engines exist is a deployment fact — so "the key names a configured engine" IS the validation.
public class EngineKeyValidationTests
{
    private static ReprocessDocumentVersionBodyValidator ReprocessValidator() =>
        new(TestTextExtraction.Engines());

    private static BulkReprocessDocumentsCommandValidator BulkValidator() =>
        new(TestTextExtraction.Engines());

    // An omitted body / null key means "use the configured default" and must stay valid.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reprocess_NoEngineKey_IsValid(string? key)
    {
        var result = ReprocessValidator().Validate(new ReprocessDocumentVersionBody(key));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Reprocess_ConfiguredKey_IsValid()
    {
        var result = ReprocessValidator().Validate(
            new ReprocessDocumentVersionBody("azure-layout"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Reprocess_UnknownKey_IsRejectedAndListsWhatExists()
    {
        var result = ReprocessValidator().Validate(
            new ReprocessDocumentVersionBody("azure-typo"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(ReprocessDocumentVersionBody.EngineKey), error.PropertyName);
        Assert.Contains("'tesseract'", error.ErrorMessage);
        Assert.Contains("'azure-layout'", error.ErrorMessage);
    }

    [Fact]
    public void Bulk_UnknownKey_IsRejected()
    {
        var result = BulkValidator().Validate(new BulkReprocessDocumentsCommand([Guid.NewGuid()], "azure-typo"));

        Assert.False(result.IsValid);
        Assert.Equal(nameof(BulkReprocessDocumentsCommand.EngineKey), Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Bulk_ConfiguredKeyOrNone_IsValid()
    {
        Assert.True(BulkValidator().Validate(new BulkReprocessDocumentsCommand([Guid.NewGuid()], "azure-layout")).IsValid);
        Assert.True(BulkValidator().Validate(new BulkReprocessDocumentsCommand([Guid.NewGuid()])).IsValid);
    }

    // Validation only asks whether the key names something real. An engine that exists but is currently
    // unavailable is caught at enqueue by the resolver (a 400 carrying the reason), not here — the two
    // checks answer different questions.
    [Fact]
    public void Reprocess_ConfiguredButUnavailableKey_PassesValidationAndIsLeftToTheResolver()
    {
        var result = ReprocessValidator().Validate(
            new ReprocessDocumentVersionBody("azure-layout"));

        Assert.True(result.IsValid);
    }
}
