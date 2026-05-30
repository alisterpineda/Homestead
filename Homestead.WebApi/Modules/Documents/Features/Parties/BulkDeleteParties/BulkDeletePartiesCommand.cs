using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.BulkDeleteParties;

public record BulkDeletePartiesCommand(List<Guid> Ids) : ICommand<BulkDeletePartiesResponse>;
