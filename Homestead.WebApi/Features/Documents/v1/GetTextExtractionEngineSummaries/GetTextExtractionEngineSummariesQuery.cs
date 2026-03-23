using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetTextExtractionEngineSummaries;

public class GetTextExtractionEngineSummariesQuery : IRequest<IReadOnlyList<TextExtractionEngineSummary>>;
