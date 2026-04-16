using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.GetDocumentSettings;

public record GetDocumentSettingsQuery : IRequest<DocumentSettingsResponse>;
