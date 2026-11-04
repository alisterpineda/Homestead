using System.Text.Json;
using System.Text.Json.Serialization;
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
    public void Serialize_Tesseract_WritesTextLayerExtractorWhenSet()
    {
        var json = Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract",
            ExtractedAt = ExtractedAt,
            Language = "eng",
            PagesFromTextLayer = 3,
            TextLayerExtractor = TesseractTextExtractionMetadata.PopplerTextLayer
        });

        Assert.Equal("poppler", json.GetProperty("textLayerExtractor").GetString());
    }

    // Images run no text-layer pass, so the key stays out of the blob rather than reading as "unknown".
    [Fact]
    public void Serialize_Tesseract_OmitsTextLayerExtractorWhenNull()
    {
        var json = Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract",
            ExtractedAt = ExtractedAt,
            Language = "eng",
            PagesOcred = 1
        });

        Assert.False(json.TryGetProperty("textLayerExtractor", out _));
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
        // This blob predates textLayerExtractor: old rows must keep reading, with the field simply null.
        Assert.Null(metadata.TextLayerExtractor);
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
    [InlineData(typeof(DoclingTextExtractionMetadata))]
    [InlineData(typeof(ManualTextExtractionMetadata))]
    public void Serialize_RoundTripsThroughTheBaseType(Type expected)
    {
        TextExtractionMetadata original = expected switch
        {
            _ when expected == typeof(ManualTextExtractionMetadata) => Manual(),
            _ when expected == typeof(TesseractTextExtractionMetadata) => new TesseractTextExtractionMetadata
            {
                EngineKey = "tesseract",
                ExtractedAt = ExtractedAt,
                Language = "eng+fra",
                TextLayerExtractor = TesseractTextExtractionMetadata.PdfPigTextLayer
            },
            _ when expected == typeof(AzureDocumentIntelligenceTextExtractionMetadata) =>
                new AzureDocumentIntelligenceTextExtractionMetadata
                {
                    EngineKey = "azure-layout",
                    ExtractedAt = ExtractedAt,
                    ModelId = AzureModels.PrebuiltLayout,
                    OutputContentFormat = AzureOutputContentFormat.Markdown
                },
            _ when expected == typeof(DoclingTextExtractionMetadata) => Docling(),
            _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, "No sample for this type.")
        };

        var back = TextExtractionMetadata.Deserialize(TextExtractionMetadata.Serialize(original));

        Assert.IsType(expected, back);
        Assert.Equal(original, back);
    }

    private static DoclingTextExtractionMetadata Docling() => new()
    {
        EngineKey = "docling",
        ExtractedAt = ExtractedAt,
        ServerVersion = "1.38.2",
        TableMode = "accurate",
        ForceOcr = false,
        PageCount = 3,
        ConversionStatus = DoclingTextExtractionMetadata.PartialSuccessStatus,
        ProcessingSeconds = 41.7
    };

    [Fact]
    public void Serialize_Docling_UsesItsSlugAndCamelCase()
    {
        var json = Serialize(Docling());

        Assert.Equal("docling", json.GetProperty("engine").GetString());
        Assert.Equal("docling", json.GetProperty("engineKey").GetString());
        Assert.Equal("1.38.2", json.GetProperty("serverVersion").GetString());
        Assert.Equal("accurate", json.GetProperty("tableMode").GetString());
        Assert.False(json.GetProperty("forceOcr").GetBoolean());
        Assert.Equal(3, json.GetProperty("pageCount").GetInt32());
        Assert.Equal("partial_success", json.GetProperty("conversionStatus").GetString());
        Assert.Equal(41.7, json.GetProperty("processingSeconds").GetDouble());
    }

    [Fact]
    public void Deserialize_DoclingWithKeysReordered_StillReads()
    {
        // As jsonb returns it: keys sorted by (length, bytes), so "engine" is not first.
        var asJsonbReturnsIt = """
            {"engine":"docling","forceOcr":false,"engineKey":"docling","pageCount":3,"tableMode":"fast",
             "extractedAt":"2026-07-15T10:34:02+00:00","serverVersion":"1.38.2",
             "conversionStatus":"success","processingSeconds":12.5}
            """;

        var metadata = Assert.IsType<DoclingTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize(asJsonbReturnsIt));

        Assert.Equal("docling", metadata.EngineKey);
        Assert.Equal("fast", metadata.TableMode);
        Assert.Equal(3, metadata.PageCount);
        Assert.Equal("success", metadata.ConversionStatus);
        Assert.Equal(12.5, metadata.ProcessingSeconds);
    }

    [Fact]
    public void ProvenanceResponse_Docling_FlattensAndLeavesOtherEnginesFieldsNull()
    {
        var response = TextExtractionProvenanceResponse.From(TextExtractionMetadata.Serialize(Docling()))!;

        Assert.Equal("docling", response.Engine);
        Assert.Equal("docling", response.EngineKey);
        Assert.Equal("1.38.2", response.ServerVersion);
        Assert.Equal("accurate", response.TableMode);
        Assert.False(response.ForceOcr);
        Assert.Equal(3, response.PageCount);
        Assert.Equal("partial_success", response.ConversionStatus);
        Assert.Equal(41.7, response.ProcessingSeconds);
        Assert.Null(response.Language);
        Assert.Null(response.PagesOcred);
        Assert.Null(response.ModelId);
        Assert.Null(response.OutputContentFormat);
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
        Assert.Null(response.TextLayerExtractor);
        Assert.Null(response.ConversionStatus);
        Assert.Null(response.ForceOcr);
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
            PagesOcred = 0,
            TextLayerExtractor = TesseractTextExtractionMetadata.PopplerTextLayer
        });

        var response = TextExtractionProvenanceResponse.From(json)!;

        Assert.Equal("tesseract", response.Engine);
        Assert.Equal(12, response.PagesFromTextLayer);
        Assert.Equal(0, response.PagesOcred);
        Assert.Equal("poppler", response.TextLayerExtractor);
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

    // --- Manual ---

    private static readonly Guid Editor = new("0199a7c1-6b5e-7c3a-9d4e-1f2a3b4c5d6e");
    private static readonly DateTimeOffset EditedAt = new(2026, 11, 4, 9, 15, 0, TimeSpan.Zero);

    private static ManualTextExtractionMetadata Manual() => new()
    {
        ExtractedAt = EditedAt,
        EditedByUserId = Editor,
        BasisEngine = "docling",
        BasisEngineKey = "docling",
        BasisExtractedAt = ExtractedAt
    };

    [Fact]
    public void Serialize_Manual_UsesManualSlugCamelCaseAndCarriesTheBasis()
    {
        var json = Serialize(Manual());

        Assert.Equal("manual", json.GetProperty("engine").GetString());
        Assert.Equal(Editor, json.GetProperty("editedByUserId").GetGuid());
        Assert.Equal(EditedAt, json.GetProperty("extractedAt").GetDateTimeOffset());
        Assert.Equal("docling", json.GetProperty("basisEngine").GetString());
        Assert.Equal("docling", json.GetProperty("basisEngineKey").GetString());
        Assert.Equal(ExtractedAt, json.GetProperty("basisExtractedAt").GetDateTimeOffset());
        // A manual record has no instance key: the engine-only fields never appear on it.
        Assert.False(json.TryGetProperty("engineKey", out _));
    }

    // Text typed in over a version with no provenance has no basis — and the keys stay out of the blob.
    [Fact]
    public void Serialize_Manual_OmitsAbsentBasis()
    {
        var json = Serialize(new ManualTextExtractionMetadata { ExtractedAt = EditedAt, EditedByUserId = Editor });

        Assert.False(json.TryGetProperty("basisEngine", out _));
        Assert.False(json.TryGetProperty("basisEngineKey", out _));
        Assert.False(json.TryGetProperty("basisExtractedAt", out _));
    }

    [Fact]
    public void Deserialize_ManualWithKeysReorderedTheWayJsonbReturnsThem_StillReads()
    {
        // As jsonb returns it: keys sorted by (length, bytes), so "engine" is not first.
        var asJsonbReturnsIt = """
            {"engine":"manual","basisEngine":"docling","extractedAt":"2026-11-04T09:15:00+00:00",
             "basisEngineKey":"docling","editedByUserId":"0199a7c1-6b5e-7c3a-9d4e-1f2a3b4c5d6e",
             "basisExtractedAt":"2026-07-15T10:34:02+00:00"}
            """;

        var metadata = Assert.IsType<ManualTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize(asJsonbReturnsIt));

        Assert.Equal(Manual(), metadata);
    }

    // Three hand-kept lists have to agree — the [JsonDerivedType] attributes (what Serialize writes), the
    // Deserialize switch, and SlugOf (which throws on a kind it does not know, on every document read).
    // This walks the attributes so a fifth kind that misses one of the other two fails here, not in
    // production.
    [Fact]
    public void EveryRegisteredKind_RoundTripsAndHasASlug()
    {
        var registered = typeof(TextExtractionMetadata)
            .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
            .Cast<JsonDerivedTypeAttribute>()
            .ToList();

        Assert.Equal(4, registered.Count);
        foreach (var attribute in registered)
        {
            var blob = $$"""{"engine":"{{attribute.TypeDiscriminator}}","extractedAt":"2026-07-15T10:34:02+00:00","engineKey":"k","language":"eng","modelId":"m","outputContentFormat":"markdown","conversionStatus":"success"}""";
            var back = TextExtractionMetadata.Deserialize(blob);

            Assert.NotNull(back);
            Assert.IsType(attribute.DerivedType, back);
            Assert.Equal(attribute.TypeDiscriminator, TextExtractionMetadata.SlugOf(back));
        }
    }

    [Fact]
    public void SlugOf_NamesEveryKind()
    {
        Assert.Equal("tesseract", TextExtractionMetadata.SlugOf(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract", ExtractedAt = ExtractedAt, Language = "eng"
        }));
        Assert.Equal("azure-document-intelligence", TextExtractionMetadata.SlugOf(
            new AzureDocumentIntelligenceTextExtractionMetadata
            {
                EngineKey = "azure-layout", ExtractedAt = ExtractedAt,
                ModelId = AzureModels.PrebuiltLayout, OutputContentFormat = AzureOutputContentFormat.Markdown
            }));
        Assert.Equal("docling", TextExtractionMetadata.SlugOf(Docling()));
        Assert.Equal("manual", TextExtractionMetadata.SlugOf(Manual()));
    }

    // The engine run becomes the basis: the kind slug (not the instance key, which could be anything),
    // the instance that ran, and when.
    [Fact]
    public void Replacing_AnEngineRun_RecordsItAsTheBasis()
    {
        var replaced = ManualTextExtractionMetadata.Replacing(
            Docling() with { EngineKey = "docling-gpu" }, Editor, EditedAt);

        Assert.Equal(Manual() with { BasisEngineKey = "docling-gpu" }, replaced);
    }

    // A second correction is still "edited Docling output": the original basis survives, only the
    // editor and time move.
    [Fact]
    public void Replacing_AnEarlierManualEdit_CarriesItsBasisForward()
    {
        var secondEditor = Guid.NewGuid();
        var later = EditedAt.AddDays(1);

        var replaced = ManualTextExtractionMetadata.Replacing(Manual(), secondEditor, later);

        Assert.Equal(Manual() with { EditedByUserId = secondEditor, ExtractedAt = later }, replaced);
    }

    [Fact]
    public void Replacing_NoProvenance_HasNoBasis()
    {
        var replaced = ManualTextExtractionMetadata.Replacing(null, Editor, EditedAt);

        Assert.Equal(new ManualTextExtractionMetadata { ExtractedAt = EditedAt, EditedByUserId = Editor }, replaced);
    }

    // The flat DTO: "manual" in the kind slot, no instance key, the basis beside it. The editor's name is
    // the reader's job (it needs the database), so the pure mapping leaves it null.
    [Fact]
    public void ProvenanceResponse_Manual_FlattensBasisAndLeavesEngineFieldsNull()
    {
        var response = TextExtractionProvenanceResponse.From(TextExtractionMetadata.Serialize(Manual()))!;

        Assert.Equal("manual", response.Engine);
        Assert.Null(response.EngineKey);
        Assert.Equal(EditedAt, response.ExtractedAt);
        Assert.Equal("docling", response.BasisEngine);
        Assert.Equal("docling", response.BasisEngineKey);
        Assert.Equal(ExtractedAt, response.BasisExtractedAt);
        Assert.Null(response.EditedByDisplayName);
        Assert.Null(response.RequestedEngineKey);
        Assert.Null(response.Language);
        Assert.Null(response.PageCount);
        Assert.Null(response.ConversionStatus);
    }

    // The engine kinds never carry the manual fields.
    [Fact]
    public void ProvenanceResponse_Engine_LeavesManualFieldsNull()
    {
        var response = TextExtractionProvenanceResponse.From(TextExtractionMetadata.Serialize(Docling()))!;

        Assert.Equal("docling", response.EngineKey);
        Assert.Null(response.EditedByDisplayName);
        Assert.Null(response.BasisEngine);
        Assert.Null(response.BasisEngineKey);
        Assert.Null(response.BasisExtractedAt);
    }
}
