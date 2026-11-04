namespace Homestead.WebApi.Modules.Mcp;

// The page-size caps of the paged tools. Out-of-range values are clamped and reported (McpWarnings.Clamp), never
// rejected: a model asking for 1000 rows gets the cap, not an error it has to recover from.
public static class McpPaging
{
    public const int SearchMaxPageSize = 100;
    public const int ListMaxPageSize = 500;
}
