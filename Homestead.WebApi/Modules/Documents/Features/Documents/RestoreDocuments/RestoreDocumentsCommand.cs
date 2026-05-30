using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.RestoreDocuments;

public record RestoreDocumentsCommand(List<Guid> Ids) : ICommand<RestoreDocumentsResponse>;
