using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.BulkDeleteAiProviders;

public record BulkDeleteAiProvidersCommand(List<Guid> Ids) : IRequest<BulkDeleteAiProvidersResponse>;
