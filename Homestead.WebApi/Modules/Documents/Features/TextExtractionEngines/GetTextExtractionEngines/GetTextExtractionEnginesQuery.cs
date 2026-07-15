using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;

public record GetTextExtractionEnginesQuery : IRequest<List<TextExtractionEngineResponse>>;
