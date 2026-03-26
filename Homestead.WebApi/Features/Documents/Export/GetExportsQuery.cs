using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.Export;

public record GetExportsQuery : PaginatedRequest, IRequest<PaginatedResponse<GetExportsResponse>>;
