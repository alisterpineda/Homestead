using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayees;

public record GetPayeesQuery : PaginatedRequest, IRequest<PaginatedResponse<PayeeResponse>>
{
    /// <summary>Case-insensitive contains search on the payee name. Serves the picker's server-search.</summary>
    public string? Search { get; init; }

    /// <summary>Column to sort by: <c>name</c> (default) | <c>transactions</c> | <c>updated</c>. Serves the management table.</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction: <c>asc</c> (default) | <c>desc</c>.</summary>
    public string? SortDir { get; init; }

    /// <summary>Include archived payees. Default false — archived payees are hidden from pickers and the default list.</summary>
    public bool? IncludeArchived { get; init; }
}
