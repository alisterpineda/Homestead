using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.BulkDeleteAiProviders;

public record BulkDeleteAiProvidersCommand(List<Guid> Ids) : ICommand<BulkDeleteAiProvidersResponse>;
