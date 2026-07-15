using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;

// Reads straight off the registered engines rather than a hand-kept list, so the picker cannot drift
// from what is actually registered — that drift is exactly what the old DB-backed engine table cost.
public class GetTextExtractionEnginesHandler(IEnumerable<ITextExtractionEngine> engines)
    : IRequestHandler<GetTextExtractionEnginesQuery, List<TextExtractionEngineResponse>>
{
    public Task<List<TextExtractionEngineResponse>> Handle(
        GetTextExtractionEnginesQuery request, CancellationToken ct)
    {
        var result = engines
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select(e => new TextExtractionEngineResponse(
                e.Key,
                DisplayName(e.Type),
                e.Type,
                e.IsConfigured,
                e.ConfigurationError))
            .ToList();

        return Task.FromResult(result);
    }

    private static string DisplayName(TextExtractionEngineType type) => type switch
    {
        TextExtractionEngineType.Tesseract => "Tesseract",
        TextExtractionEngineType.AzureDocumentIntelligence => "Azure Document Intelligence",
        _ => type.ToString()
    };
}
