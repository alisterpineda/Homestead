namespace Homestead.WebApi.Modules.Mcp;

// Page/page-size clamping for the paged tools. Out-of-range values are clamped rather than rejected: a model asking
// for 1000 rows gets the cap, not an error it has to recover from.
public static class McpPaging
{
    public const int SearchMaxPageSize = 100;
    public const int ListMaxPageSize = 500;

    public static int ClampPage(int page) => Math.Max(1, page);

    public static int ClampPageSize(int pageSize, int max) => Math.Clamp(pageSize, 1, max);
}
