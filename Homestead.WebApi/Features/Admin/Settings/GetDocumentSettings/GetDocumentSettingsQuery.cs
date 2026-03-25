using Homestead.WebApi.Features.Admin.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.Settings.GetDocumentSettings;

public record GetDocumentSettingsQuery : IRequest<DocumentSettingsResponse>;
