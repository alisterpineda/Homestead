using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines;

public record OcrEngineResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required OcrEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
    public required bool IsBuiltIn { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }

    public static readonly OcrEngineResponse TesseractDefault = new()
    {
        Id = BuiltInOcrEngines.TesseractDefaultId,
        Name = BuiltInOcrEngines.TesseractDefaultName,
        EngineType = OcrEngineType.Tesseract,
        Configuration = BuiltInOcrEngines.TesseractDefaultConfiguration,
        IsBuiltIn = true,
        CreatedAt = DateTimeOffset.MinValue,
    };
}
