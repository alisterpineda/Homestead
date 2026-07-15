namespace Homestead.WebApi.Domain.Settings;

public enum TextExtractionEngineType
{
    Tesseract,
    AzureDocumentIntelligence
}

// Output shape requested from Azure Document Intelligence. Our own enum rather than the SDK's
// DocumentContentFormat, so neither config binding nor the persisted provenance shape depends on the
// Azure package. Lives here (rather than in Infrastructure) so the Domain metadata record and the
// Infrastructure options bag can share one definition.
public enum AzureOutputContentFormat
{
    Text,
    Markdown
}
