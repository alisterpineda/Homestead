using System.Text.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Features.Versions;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The persisted jsonb shape. It is a stored contract read back by the version-detail endpoint, so the
// discriminator and field names are pinned here rather than left to whatever the serializer does.
public class TextExtractionMetadataTests
{
    private static readonly DateTimeOffset ExtractedAt =
        new(2026, 7, 15, 10, 34, 2, TimeSpan.Zero);

    private static JsonElement Serialize(TextExtractionMetadata metadata) =>
        JsonDocument.Parse(TextExtractionMetadata.Serialize(metadata)!).RootElement;

    [Fact]
    public void Serialize_Tesseract_UsesTypeSlugDiscriminatorAndCamelCase()
    {
        var json = Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract",
            ExtractedAt = ExtractedAt,
            Language = "eng",
            Dpi = 300,
            Psm = 3,
            Oem = 3,
            PagesFromTextLayer = 12,
            PagesOcred = 0
        });

        Assert.Equal("tesseract", json.GetProperty("engine").GetString());
        Assert.Equal("tesseract", json.GetProperty("engineKey").GetString());
        Assert.Equal("eng", json.GetProperty("language").GetString());
        // The pair that makes the hybrid PDF branch visible after the fact.
        Assert.Equal(12, json.GetProperty("pagesFromTextLayer").GetInt32());
        Assert.Equal(0, json.GetProperty("pagesOcred").GetInt32());
    }

    [Fact]
    public void Serialize_Azure_WritesTypeSlugKeyAndEnumAsCamelCaseString()
    {
        var json = Serialize(new AzureDocumentIntelligenceTextExtractionMetadata
        {
            EngineKey = "azure-layout",
            ExtractedAt = ExtractedAt,
            ModelId = AzureModels.PrebuiltLayout,
            OutputContentFormat = AzureOutputContentFormat.Markdown,
            PageCount = 12
        });

        // The discriminator is the engine TYPE; the instance that ran is a field.
        Assert.Equal("azure-document-intelligence", json.GetProperty("engine").GetString());
        Assert.Equal("azure-layout", json.GetProperty("engineKey").GetString());
        Assert.Equal("prebuilt-layout", json.GetProperty("modelId").GetString());
        Assert.Equal("markdown", json.GetProperty("outputContentFormat").GetString());
        Assert.Equal(12, json.GetProperty("pageCount").GetInt32());
    }

    // Fallback fields are the exception rather than the norm, so they stay out of the blob when unset.
    [Fact]
    public void Serialize_NoFallback_OmitsFallbackFields()
    {
        var json = Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract",
            ExtractedAt = ExtractedAt,
            Language = "eng"
        });

        Assert.False(json.TryGetProperty("requestedEngineKey", out _));
        Assert.False(json.TryGetProperty("fallbackReason", out _));
    }

    [Fact]
    public void Serialize_Fallback_RecordsWhatWasAskedForAndWhy()
    {
        var json = Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract",
            ExtractedAt = ExtractedAt,
            Language = "eng",
            RequestedEngineKey = "azure-layout",
            FallbackReason = "Endpoint is required."
        });

        Assert.Equal("azure-layout", json.GetProperty("requestedEngineKey").GetString());
        Assert.Equal("Endpoint is required.", json.GetProperty("fallbackReason").GetString());
    }

    // THE regression guard for this column. Postgres jsonb does not store the bytes we hand it: it
    // reparses and stores keys sorted by (length, bytes), so "dpi"/"oem"/"psm" come back BEFORE the
    // "engine" discriminator. System.Text.Json's polymorphic deserializer requires the discriminator to
    // be the first property and throws NotSupportedException otherwise — which is exactly what a plain
    // Deserialize<TextExtractionMetadata>() did here, on every document read, in production shape only.
    // A round-trip of our own serializer output cannot catch this, because our output IS ordered.
    [Fact]
    public void Deserialize_KeysReorderedTheWayJsonbReturnsThem_StillReads()
    {
        var asJsonbReturnsIt = """
            {"dpi":300,"oem":3,"psm":3,"engine":"tesseract","language":"eng","engineKey":"tesseract",
             "pagesOcred":0,"extractedAt":"2026-07-15T10:34:02+00:00","pagesFromTextLayer":12}
            """;

        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize(asJsonbReturnsIt));

        Assert.Equal("tesseract", metadata.EngineKey);
        Assert.Equal(12, metadata.PagesFromTextLayer);
        Assert.Equal(300, metadata.Dpi);
    }

    [Fact]
    public void Deserialize_AzureWithKeysReordered_StillReads()
    {
        var asJsonbReturnsIt = """
            {"engine":"azure-document-intelligence","modelId":"prebuilt-layout","engineKey":"azure-layout",
             "pageCount":12,"extractedAt":"2026-07-15T10:34:02+00:00","outputContentFormat":"markdown"}
            """;

        var metadata = Assert.IsType<AzureDocumentIntelligenceTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize(asJsonbReturnsIt));

        Assert.Equal("azure-layout", metadata.EngineKey);
        Assert.Equal(AzureOutputContentFormat.Markdown, metadata.OutputContentFormat);
        Assert.Equal(12, metadata.PageCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"engineKey":"tesseract"}""")]
    [InlineData("""{"engine":"some-engine-we-removed"}""")]
    [InlineData("""{"engine":42}""")]
    public void Deserialize_MissingOrUnreadable_ReturnsNull(string? json)
    {
        Assert.Null(TextExtractionMetadata.Deserialize(json));
    }

    [Theory]
    [InlineData(typeof(TesseractTextExtractionMetadata))]
    [InlineData(typeof(AzureDocumentIntelligenceTextExtractionMetadata))]
    public void Serialize_RoundTripsThroughTheBaseType(Type expected)
    {
        TextExtractionMetadata original = expected == typeof(TesseractTextExtractionMetadata)
            ? new TesseractTextExtractionMetadata
            { EngineKey = "tesseract", ExtractedAt = ExtractedAt, Language = "eng+fra" }
            : new AzureDocumentIntelligenceTextExtractionMetadata
            {
                EngineKey = "azure-layout",
                ExtractedAt = ExtractedAt,
                ModelId = AzureModels.PrebuiltLayout,
                OutputContentFormat = AzureOutputContentFormat.Markdown
            };

        var back = TextExtractionMetadata.Deserialize(TextExtractionMetadata.Serialize(original));

        Assert.IsType(expected, back);
        Assert.Equal(original, back);
    }

    // The API DTO is flat rather than a union, because ng-openapi-gen turns a polymorphic hierarchy into
    // an awkward TS union for what is read-only display data.
    [Fact]
    public void ProvenanceResponse_Azure_FlattensAndLeavesTesseractFieldsNull()
    {
        var json = TextExtractionMetadata.Serialize(new AzureDocumentIntelligenceTextExtractionMetadata
        {
            EngineKey = "azure-layout",
            ExtractedAt = ExtractedAt,
            ModelId = AzureModels.PrebuiltLayout,
            OutputContentFormat = AzureOutputContentFormat.Markdown,
            PageCount = 12
        });

        var response = TextExtractionProvenanceResponse.From(json)!;

        Assert.Equal("azure-document-intelligence", response.Engine);
        Assert.Equal("azure-layout", response.EngineKey);
        Assert.Equal(12, response.PageCount);
        Assert.Null(response.Language);
        Assert.Null(response.PagesOcred);
    }

    [Fact]
    public void ProvenanceResponse_Tesseract_FlattensAndLeavesAzureFieldsNull()
    {
        var json = TextExtractionMetadata.Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract",
            ExtractedAt = ExtractedAt,
            Language = "eng",
            PagesFromTextLayer = 12,
            PagesOcred = 0
        });

        var response = TextExtractionProvenanceResponse.From(json)!;

        Assert.Equal("tesseract", response.Engine);
        Assert.Equal(12, response.PagesFromTextLayer);
        Assert.Equal(0, response.PagesOcred);
        Assert.Null(response.ModelId);
        Assert.Null(response.PageCount);
    }

    // Versions extracted before provenance existed, and blobs whose shape has moved on, are display-only
    // data — not worth failing a read for.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"engine":"some-engine-we-removed"}""")]
    public void ProvenanceResponse_MissingOrUnreadable_ReturnsNull(string? json)
    {
        Assert.Null(TextExtractionProvenanceResponse.From(json));
    }
}
