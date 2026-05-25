using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.AiProviderSummaries.GetAiProviderSummaries;

public class GetAiProviderSummariesQuery : IRequest<IReadOnlyList<AiProviderSummary>>;
