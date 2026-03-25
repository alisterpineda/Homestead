using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines;

public record TextExtractionEngineResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required TextExtractionEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
    public required bool IsBuiltIn { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }

    public static readonly TextExtractionEngineResponse TesseractDefault = new()
    {
        Id = BuiltInTextExtractionEngines.TesseractDefaultId,
        Name = BuiltInTextExtractionEngines.TesseractDefaultName,
        EngineType = TextExtractionEngineType.Tesseract,
        Configuration = BuiltInTextExtractionEngines.TesseractDefaultConfiguration,
        IsBuiltIn = true,
        CreatedAt = DateTimeOffset.MinValue,
    };
}
