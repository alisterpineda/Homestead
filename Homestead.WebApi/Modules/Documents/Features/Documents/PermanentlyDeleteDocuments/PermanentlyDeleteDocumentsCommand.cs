using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PermanentlyDeleteDocuments;

public record PermanentlyDeleteDocumentsCommand(List<Guid> Ids) : ICommand<PermanentlyDeleteDocumentsResponse>;
