using MediatR;

namespace Homestead.WebApi.Features.Documents.Settings.GetDocumentSettings;

public record GetDocumentSettingsQuery : IRequest<DocumentSettingsResponse>;
