using System.Text.RegularExpressions;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

/// <summary>
/// The exit door stays one door: IAiProvider is the only way the WebApi talks to a model. This source scan fails
/// on a Microsoft.Extensions.AI chat abstraction anywhere in the WebApi (only AiJsonSchema may use the package, for
/// its schema generator), and on an Anthropic SDK type outside Infrastructure/Ai/ — so swapping the backend stays
/// a rewrite of one folder.
/// </summary>
public partial class AiSeamIsolationTests
{
    private const string AiFolder = "Infrastructure/Ai/";
    private const string SchemaFile = "Infrastructure/Ai/AiJsonSchema.cs";

    [GeneratedRegex(@"using\s+Microsoft\.Extensions\.AI\b|\bIChatClient\b|\bChatOptions\b|\bChatMessage\b")]
    private static partial Regex MeaiChatAbstraction();

    [GeneratedRegex(@"^\s*using\s+(static\s+)?Anthropic(\.|;)|\bAnthropic\.(Models|Exceptions|Core|Helpers)\b|\bAnthropicClient\b",
        RegexOptions.Multiline)]
    private static partial Regex AnthropicSdk();

    private static List<(string Path, string Text)> WebApiSources()
    {
        var root = FindWebApiRoot();

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();
    }

    [Fact]
    public void SourceScan_FindsTheSeam()
    {
        var paths = WebApiSources().Select(s => s.Path).ToList();

        // Guards the guard: a scan that stopped matching would let the assertions below pass vacuously.
        Assert.Contains(SchemaFile, paths);
        Assert.Contains("Infrastructure/Ai/AnthropicAiProvider.cs", paths);
        Assert.Matches(AnthropicSdk(), File.ReadAllText(Path.Combine(FindWebApiRoot(), "Infrastructure/Ai/AnthropicAiProvider.cs")));
    }

    [Fact]
    public void OnlyAiJsonSchema_UsesMicrosoftExtensionsAi()
    {
        var offenders = WebApiSources()
            .Where(s => s.Path != SchemaFile && MeaiChatAbstraction().IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Talk to the model through IAiProvider, not Microsoft.Extensions.AI: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void OnlyInfrastructureAi_UsesTheAnthropicSdk()
    {
        var offenders = WebApiSources()
            .Where(s => !s.Path.StartsWith(AiFolder, StringComparison.Ordinal) && AnthropicSdk().IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Anthropic SDK types belong behind IAiProvider in {AiFolder}: {string.Join(", ", offenders)}");
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
