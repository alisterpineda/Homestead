namespace Homestead.WebApi.Common;

public record PaginatedRequest
{
    private int _page = 1;
    private int _pageSize = 50;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? 1 : value > 1000 ? 1000 : value;
    }
}
