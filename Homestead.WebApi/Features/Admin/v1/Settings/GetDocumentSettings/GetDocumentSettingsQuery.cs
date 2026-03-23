using Homestead.WebApi.Features.Admin.v1.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;

public record GetDocumentSettingsQuery : IRequest<DocumentSettingsResponse>;
