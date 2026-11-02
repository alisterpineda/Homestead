namespace Homestead.WebApi.Tests.Unit.Common;

/// <summary>
/// Endpoints inject their handler and call <c>HandleAsync</c> directly; job handlers wrap internal commands in
/// <c>IUnitOfWork</c> themselves; MCP tools (<c>Modules/Mcp/*Tools.cs</c>) inject handlers and call them inside
/// <c>McpToolInvoker</c>. Nothing dispatches through a mediator, so go-to-definition from an endpoint or tool
/// reaches its handler and a missing registration fails the handler-resolution guard. This source scan keeps
/// a mediator from creeping back into any of those places.
/// </summary>
public class EndpointDispatchConventionTests
{
    private static readonly string[] ForbiddenTokens = ["ISender", "IMediator", "using MediatR"];

    private static List<string> DispatchSites()
    {
        var root = FindWebApiRoot();

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => f.EndsWith("Endpoint.cs", StringComparison.Ordinal)
                        || f.EndsWith("JobHandler.cs", StringComparison.Ordinal)
                        || f.EndsWith("Tools.cs", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public void DispatchSiteScan_FindsTheEndpointsJobHandlersAndMcpTools()
    {
        var names = DispatchSites().Select(Path.GetFileName).ToList();

        // Guards the guard: a scan that stopped matching would let the assertion below pass vacuously.
        Assert.Contains("PatchTagEndpoint.cs", names);
        Assert.Contains("ClassifyDocumentJobHandler.cs", names);
        Assert.Contains("DocumentWriteTools.cs", names);
        Assert.True(names.Count > 150, $"Expected > 150 endpoint and job-handler files, found {names.Count}.");
    }

    [Fact]
    public void EndpointsJobHandlersAndMcpTools_DoNotDispatchThroughAMediator()
    {
        var root = FindWebApiRoot();

        var offenders = DispatchSites()
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .SelectMany(f => ForbiddenTokens.Where(f.Text.Contains).Select(token => $"{f.Path} ({token})"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Endpoints/job handlers/MCP tools must call their handler directly: {string.Join(", ", offenders)}");
    }

    private static string FindWebApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Homestead.WebApi", "Homestead.WebApi.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Homestead.WebApi from the test output directory.");
    }
}
