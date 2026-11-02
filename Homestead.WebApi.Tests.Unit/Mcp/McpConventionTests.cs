using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Homestead.WebApi.Tests.Unit.Mcp;

/// <summary>
/// The MCP SDK stays in one folder: tool classes are <c>*Tools</c> under <c>Modules/Mcp/</c> (never <c>*Handler</c>,
/// which <c>AddHandlers()</c> and the handler guard tests would pick up), no file outside that folder imports the SDK
/// (<c>Program.cs</c> calls <c>AddHomesteadMcp</c>/<c>MapHomesteadMcp</c> only), and the folder never imports
/// <c>Microsoft.Extensions.AI</c> (the SDK depends on it, but the AI seam owns that package).
/// <para>
/// The write rules scan <c>Modules/Mcp/*Tools.cs</c> per method: every handler call sits lexically inside the
/// <c>McpToolInvoker</c> frame its kind requires (a private helper's handler call counts at each of its call sites),
/// no tool touches the unit of work, the audit pipeline or a validator, every tool sets its hints explicitly, and
/// every write tool carries an authorization policy.
/// </para>
/// </summary>
public partial class McpConventionTests
{
    private const string McpFolder = "Modules/Mcp/";
    private const string WhoAmIFile = "Modules/Mcp/WhoAmITools.cs";
    private const string ReadToolsFile = "Modules/Mcp/DocumentTools.cs";
    private const string WriteToolsFile = "Modules/Mcp/DocumentWriteTools.cs";

    [GeneratedRegex(@"\[\s*McpServerToolType\s*\][^{;]*?\bclass\s+(?<name>\w+)")]
    private static partial Regex ToolTypeClass();

    [GeneratedRegex(@"^\s*(global\s+)?using\s+(static\s+)?(\w+\s*=\s*)?ModelContextProtocol\b", RegexOptions.Multiline)]
    private static partial Regex UsingMcpSdk();

    [GeneratedRegex(@"^\s*(global\s+)?using\s+(static\s+)?(\w+\s*=\s*)?Microsoft\.Extensions\.AI\b", RegexOptions.Multiline)]
    private static partial Regex UsingMeai();

    private static List<(string Path, string Text)> WebApiSources()
    {
        var root = FindWebApiRoot();

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();
    }

    private static List<(string Path, string Name)> ToolTypes() =>
        WebApiSources()
            .SelectMany(s => ToolTypeClass().Matches(s.Text).Select(m => (s.Path, m.Groups["name"].Value)))
            .ToList();

    // Modules/Mcp/*Tools.cs, directly in the folder.
    private static List<(string Path, string Text)> ToolSources() =>
        WebApiSources()
            .Where(s => s.Path.StartsWith(McpFolder, StringComparison.Ordinal)
                        && !s.Path[McpFolder.Length..].Contains('/')
                        && s.Path.EndsWith("Tools.cs", StringComparison.Ordinal))
            .ToList();

    private static List<ToolClass> ScanToolClasses() =>
        ToolSources().SelectMany(s => ToolSourceScanner.Scan(s.Path, s.Text)).ToList();

    [Fact]
    public void SourceScan_FindsTheWhoAmITool()
    {
        // Guards the guard: a scan that stopped matching would let the assertions below pass vacuously.
        Assert.Contains(WhoAmIFile, WebApiSources().Select(s => s.Path));
        Assert.Contains((WhoAmIFile, "WhoAmITools"), ToolTypes());
    }

    [Fact]
    public void ToolTypes_AreNamedTools_AndLiveUnderModulesMcp()
    {
        var offenders = ToolTypes()
            .Where(t => !t.Name.EndsWith("Tools", StringComparison.Ordinal)
                        || !t.Path.StartsWith(McpFolder, StringComparison.Ordinal))
            .Select(t => $"{t.Path}: {t.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"[McpServerToolType] classes are named *Tools and live in {McpFolder}: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void OnlyModulesMcp_UsesTheMcpSdk()
    {
        var offenders = WebApiSources()
            .Where(s => !s.Path.StartsWith(McpFolder, StringComparison.Ordinal) && UsingMcpSdk().IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"ModelContextProtocol types belong in {McpFolder}: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ModulesMcp_DoesNotUseMicrosoftExtensionsAi()
    {
        var offenders = WebApiSources()
            .Where(s => s.Path.StartsWith(McpFolder, StringComparison.Ordinal) && UsingMeai().IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{McpFolder} must not use Microsoft.Extensions.AI: {string.Join(", ", offenders)}");
    }

    // --- write rules ---

    [Fact]
    public void ToolScan_FindsTheReadAndWriteTools_AndTheirHandlerCalls()
    {
        // Guards the guard: the write rules below pass vacuously if the scan stops finding files, tools or calls.
        var classes = ScanToolClasses();
        var calls = classes.SelectMany(c => c.HandlerCalls).ToList();

        Assert.Equal(
            (ReadTools: true, WriteTools: true, WriteMethods: true, PatchUnderWriteDocument: true, SearchUnderRead: true,
             ReadsUnderReadDocument: true),
            (ReadTools: classes.Any(c => c.Path == ReadToolsFile && c.Tools.Count > 0),
             WriteTools: classes.Any(c => c.Path == WriteToolsFile && c.Tools.Count > 0),
             WriteMethods: classes.SelectMany(c => c.Tools).Any(t => t.ReadOnly == false),
             PatchUnderWriteDocument: calls.Any(c => c is { Handler: "PatchDocumentHandler", Frame: "WriteDocumentAsync" }),
             SearchUnderRead: calls.Any(c => c is { Handler: "GetDocumentsHandler", Frame: "ReadAsync" }),
             ReadsUnderReadDocument: calls.Any(c => c is { Handler: "McpDocumentReads", Frame: "ReadDocumentAsync" })));
    }

    [Fact]
    public void ToolScan_ScansEveryToolType()
    {
        // Guards the guard: a [McpServerToolType] the syntax scan skips (a record, a struct) would escape every write
        // rule. The regex type list and the scanned types must name the same types in the same files.
        Assert.Equal(
            ToolTypes().Select(t => $"{t.Path}: {t.Name}").Order(),
            ScanToolClasses().Select(c => $"{c.Path}: {c.Name}").Order());
    }

    [Fact]
    public void HandlerCalls_RunInsideTheInvokerFrameTheirKindRequires()
    {
        var offenders = FrameProblems(ScanToolClasses())
            .Select(x => $"{x.Call.Path}:{x.Call.Line} {x.Call.Member} → {x.Call.Handler}: {x.Problem}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Handler calls in *Tools.cs run inside the work delegate of an McpToolInvoker call: unscoped Get* " +
            "handlers under any frame, other unscoped handlers under WriteAsync / WriteDocumentAsync; GetDocumentHandler " +
            "and McpDocumentReads under ReadDocumentAsync / WriteDocumentAsync; every other document-id handler " +
            "(PatchDocumentHandler, CreateCommentHandler, the suggestion handlers) under WriteDocumentAsync only; " +
            "the self-authorizing bulk handlers under WriteAsync / WriteDocumentAsync:\n" +
            string.Join("\n", offenders));
    }

    private static IEnumerable<(HandlerCall Call, string Problem)> FrameProblems(IEnumerable<ToolClass> classes) =>
        classes
            .SelectMany(c => c.HandlerCalls)
            .Select(call => (call, Problem: HandlerRules.Check(call.Handler, call.Frame)))
            .Where(x => x.Problem is not null)
            .Select(x => (x.call, x.Problem!));

    // --- the frame rule fed known sources (guards the guard: each shape below must be flagged, or not) ---

    private const string SampleToolsFile = "Modules/Mcp/SampleTools.cs";

    // "Member → Handler: problem" for each call the frame rule rejects in an inline *Tools.cs source.
    private static List<string> FrameFindings(string source) =>
        FrameProblems(ToolSourceScanner.Scan(SampleToolsFile, source))
            .Select(x => $"{x.Call.Member} → {x.Call.Handler}: {x.Problem}")
            .ToList();

    [Fact]
    public void FrameRule_FlagsADocumentScopedHandlerCalledOutsideEveryFrame()
    {
        // The read tool's ReadAsync frame comes first, so a frame that over-reached would swallow the later call.
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, GetTagsHandler getTags, PatchDocumentHandler patchDocument)
            {
                [McpServerTool(Name = "list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
                public Task<int> List(CancellationToken ct = default) =>
                    invoker.ReadAsync(async c => (await getTags.HandleAsync(new(), c)).TotalCount, ct);

                [McpServerTool(Name = "patch", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public async Task<int> Patch(Guid document_id, CancellationToken ct = default)
                {
                    await patchDocument.HandleAsync(document_id, new(), ct);
                    return 1;
                }
            }
            """;

        Assert.Equal(
            ["Patch → PatchDocumentHandler: called outside the invoker; allowed frames: WriteDocumentAsync"],
            FrameFindings(source));
    }

    [Fact]
    public void FrameRule_FlagsADocumentScopedHandlerUnderPlainWriteAsync()
    {
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, PatchDocumentHandler patchDocument)
            {
                [McpServerTool(Name = "patch", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Patch(Guid document_id, CancellationToken ct = default) =>
                    invoker.WriteAsync(async c =>
                    {
                        await patchDocument.HandleAsync(document_id, new(), c);
                        return 1;
                    }, ct);
            }
            """;

        Assert.Equal(
            ["Patch → PatchDocumentHandler: called under WriteAsync; allowed frames: WriteDocumentAsync"],
            FrameFindings(source));
    }

    [Fact]
    public void FrameRule_FlagsAHelperCalledFromInsideAndOutsideAFrame_AtTheOutsideCallSite()
    {
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, PatchDocumentHandler patchDocument)
            {
                [McpServerTool(Name = "inside", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Inside(Guid document_id, CancellationToken ct = default) =>
                    invoker.WriteDocumentAsync(document_id, c => PatchAsync(document_id, c), ct);

                [McpServerTool(Name = "outside", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Outside(Guid document_id, CancellationToken ct = default) =>
                    PatchAsync(document_id, ct);

                private async Task<int> PatchAsync(Guid id, CancellationToken ct)
                {
                    await patchDocument.HandleAsync(id, new(), ct);
                    return 1;
                }
            }
            """;

        Assert.Equal(
            ["PatchAsync → PatchDocumentHandler: called outside the invoker; allowed frames: WriteDocumentAsync"],
            FrameFindings(source));
    }

    // The remaining branches of HandlerRules.Check: a document-scoped Get, an unscoped write, an unknown handler.
    [Theory]
    [InlineData("GetDocumentHandler", "called under ReadAsync; allowed frames: ReadDocumentAsync / WriteDocumentAsync")]
    [InlineData("CreateTagHandler", "called under ReadAsync; allowed frames: WriteAsync / WriteDocumentAsync")]
    [InlineData("FooHandler", "unclassified handler — add it to McpConventionTests.HandlerRules (does it take a document id?)")]
    public void FrameRule_FlagsTheOtherHandlerKindsUnderPlainReadAsync(string handler, string problem)
    {
        var source = $$"""
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, {{handler}} handler)
            {
                [McpServerTool(Name = "one", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
                public Task<int> One(Guid document_id, CancellationToken ct = default) =>
                    invoker.ReadAsync(async c =>
                    {
                        await handler.HandleAsync(document_id, c);
                        return 1;
                    }, ct);
            }
            """;

        Assert.Equal([$"One → {handler}: {problem}"], FrameFindings(source));
    }

    // McpDocumentReads is no handler, but it reads one document by id with no access check of its own, so the scan
    // follows it like a document-scoped handler: only the id-authorizing frames may call it.
    [Theory]
    [InlineData("ReadAsync", "One → McpDocumentReads: called under ReadAsync; allowed frames: ReadDocumentAsync / WriteDocumentAsync")]
    [InlineData("ReadDocumentAsync", "")]
    [InlineData(null, "CountAsync → McpDocumentReads: called outside the invoker; allowed frames: ReadDocumentAsync / WriteDocumentAsync")]
    public void FrameRule_FollowsMcpDocumentReads_AsADocumentScopedRead(string? frame, string findings)
    {
        var body = frame switch
        {
            "ReadAsync" => "invoker.ReadAsync(async c => (await documentReads.DetailsAsync(document_id, c))!.VersionCount, ct)",
            "ReadDocumentAsync" =>
                "invoker.ReadDocumentAsync(document_id, async c => (await documentReads.DetailsAsync(document_id, c))!.VersionCount, ct)",
            _ => """
                 CountAsync(document_id, ct);

                     private async Task<int> CountAsync(Guid id, CancellationToken ct) =>
                         (await documentReads.DetailsAsync(id, ct))!.VersionCount
                 """,
        };
        var source = $$"""
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, McpDocumentReads documentReads)
            {
                [McpServerTool(Name = "one", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
                public Task<int> One(Guid document_id, CancellationToken ct = default) =>
                    {{body}};
            }
            """;

        var calls = ToolSourceScanner.Scan(SampleToolsFile, source).SelectMany(c => c.HandlerCalls).ToList();

        // Seen guards the guard: an accepted call must still be found, or the empty findings would prove nothing.
        Assert.Equal(
            (Seen: $"McpDocumentReads@{frame}", Findings: findings),
            (Seen: string.Join(", ", calls.Select(c => $"{c.Handler}@{c.Frame}")),
             Findings: string.Join("\n", FrameFindings(source))));
    }

    [Fact]
    public void FrameRule_AcceptsEveryHandlerInTheFrameItsKindAllows()
    {
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(
                McpToolInvoker invoker, GetTagsHandler getTags, PatchDocumentHandler patchDocument,
                BulkDeleteDocumentsHandler bulkDelete)
            {
                [McpServerTool(Name = "list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
                public Task<int> List(CancellationToken ct = default) =>
                    invoker.ReadAsync(async c => (await getTags.HandleAsync(new(), c)).TotalCount, ct);

                [McpServerTool(Name = "patch", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Patch(Guid document_id, CancellationToken ct = default) =>
                    invoker.WriteDocumentAsync(document_id, async c =>
                    {
                        await patchDocument.HandleAsync(document_id, new(), c);
                        return 1;
                    }, ct);

                [McpServerTool(Name = "trash", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Trash(Guid[] document_ids, CancellationToken ct = default) =>
                    invoker.WriteAsync(async c =>
                    {
                        await bulkDelete.HandleAsync(new(document_ids), c);
                        return 1;
                    }, ct);
            }
            """;

        var calls = ToolSourceScanner.Scan(SampleToolsFile, source).SelectMany(c => c.HandlerCalls).ToList();

        // The positive control: all three calls are seen, each in its own frame, and none is flagged.
        Assert.Equal(
            ("GetTagsHandler@ReadAsync, PatchDocumentHandler@WriteDocumentAsync, BulkDeleteDocumentsHandler@WriteAsync", 0),
            (string.Join(", ", calls.Select(c => $"{c.Handler}@{c.Frame}")), FrameFindings(source).Count));
    }

    [Fact]
    public void FrameRule_FlagsAHandlerCalledInsideAnInterpolatedStringHole_OutsideEveryFrame()
    {
        // A text scan that blanks string literals blanks the holes too; the call in the hole still runs.
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, GetTagsHandler getTags)
            {
                [McpServerTool(Name = "describe", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
                public async Task<string> Describe(CancellationToken ct = default) =>
                    $"{(await getTags.HandleAsync(new(), ct)).TotalCount} tags";
            }
            """;

        Assert.Equal(
            ["Describe → GetTagsHandler: called outside the invoker; allowed frames: ReadAsync / ReadDocumentAsync / WriteAsync / WriteDocumentAsync"],
            FrameFindings(source));
    }

    [Fact]
    public void FrameRule_AcceptsAHandlerCalledInANestedLambdaInsideItsFrame()
    {
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, PatchDocumentHandler patchDocument)
            {
                [McpServerTool(Name = "patch", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Patch(Guid document_id, CancellationToken ct = default) =>
                    invoker.WriteDocumentAsync(document_id, async c =>
                    {
                        Func<Task> patch = async () => await patchDocument.HandleAsync(document_id, new(), c);
                        await patch();
                        return 1;
                    }, ct);
            }
            """;

        var calls = ToolSourceScanner.Scan(SampleToolsFile, source).SelectMany(c => c.HandlerCalls).ToList();

        // Seen guards the guard: the call must be found in its frame, or the empty findings would prove nothing.
        Assert.Equal(
            ("PatchDocumentHandler@WriteDocumentAsync", 0),
            (string.Join(", ", calls.Select(c => $"{c.Handler}@{c.Frame}")), FrameFindings(source).Count));
    }

    [Fact]
    public void FrameRule_ScansARecordClassToolType()
    {
        const string source = """
            [McpServerToolType]
            public sealed record class SampleTools(McpToolInvoker invoker, PatchDocumentHandler patchDocument)
            {
                [McpServerTool(Name = "patch", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public async Task<int> Patch(Guid document_id, CancellationToken ct = default)
                {
                    await patchDocument.HandleAsync(document_id, new(), ct);
                    return 1;
                }
            }
            """;

        Assert.Equal(
            ["Patch → PatchDocumentHandler: called outside the invoker; allowed frames: WriteDocumentAsync"],
            FrameFindings(source));
    }

    [Fact]
    public void FrameRule_FlagsAHandlerCalledInTheInvokersIdArgument_AsOutsideTheInvoker()
    {
        // The id and ct arguments are evaluated before the frame authorizes; only the work delegate runs inside it.
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, CreateCommentHandler createComment)
            {
                [McpServerTool(Name = "comment", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public async Task<int> Comment(CancellationToken ct = default) =>
                    await invoker.WriteDocumentAsync((await createComment.HandleAsync(new(), ct)).DocumentId, c => Task.FromResult(1), ct);
            }
            """;

        Assert.Equal(
            ["Comment → CreateCommentHandler: called outside the invoker; allowed frames: WriteDocumentAsync"],
            FrameFindings(source));
    }

    [Fact]
    public void FrameRule_AcceptsAHelperPassedAsTheInvokersWorkMethodGroup()
    {
        const string source = """
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker, PatchDocumentHandler patchDocument)
            {
                [McpServerTool(Name = "patch", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
                 Authorize(Policy = AuthPolicies.RequireContributor)]
                public Task<int> Patch(Guid document_id, CancellationToken ct = default) =>
                    invoker.WriteDocumentAsync(document_id, PatchAsync, ct);

                private async Task<int> PatchAsync(CancellationToken ct)
                {
                    await patchDocument.HandleAsync(Guid.Empty, new(), ct);
                    return 1;
                }
            }
            """;

        var calls = ToolSourceScanner.Scan(SampleToolsFile, source).SelectMany(c => c.HandlerCalls).ToList();

        Assert.Equal(
            ("PatchDocumentHandler@WriteDocumentAsync", 0),
            (string.Join(", ", calls.Select(c => $"{c.Handler}@{c.Frame}")), FrameFindings(source).Count));
    }

    // --- the attribute rules fed known sources ---

    private const string AllHints = "ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false";

    // The one tool method of an inline *Tools.cs source whose method carries these attribute lists.
    private static ToolMethod ScanOneTool(string attributes) =>
        Assert.Single(ToolSourceScanner.Scan(SampleToolsFile, $$"""
            [McpServerToolType]
            public sealed class SampleTools(McpToolInvoker invoker)
            {
                {{attributes}}
                public Task<int> One(CancellationToken ct = default) => invoker.WriteAsync(c => Task.FromResult(1), ct);
            }
            """).SelectMany(c => c.Tools));

    [Theory]
    [InlineData($"[McpServerTool(Name = \"one\", {AllHints})]", false)]
    [InlineData($"[McpServerTool(Name = \"one\", {AllHints})] [Authorize]", false)]
    [InlineData($"[McpServerTool(Name = \"one\", {AllHints}), Authorize(Policy = AuthPolicies.RequireContributor)]", true)]
    [InlineData($"[McpServerTool(Name = \"one\", {AllHints})]\n[Authorize(Policy = AuthPolicies.RequireContributor)]", true)]
    public void AttributeScan_SeesAnAuthorizePolicy_OnlyWhenOneIsNamed(string attributes, bool hasPolicy)
    {
        var tool = ScanOneTool(attributes);

        Assert.Equal((ReadOnly: (bool?)false, HasAuthorizePolicy: hasPolicy), (tool.ReadOnly, tool.HasAuthorizePolicy));
    }

    [Fact]
    public void AttributeScan_ReportsAnOmittedHint()
    {
        var tool = ScanOneTool(
            "[McpServerTool(Name = \"one\", ReadOnly = false, Destructive = true, Idempotent = true)," +
            " Authorize(Policy = AuthPolicies.RequireContributor)]");

        Assert.Equal(["OpenWorld"], tool.MissingHints);
    }

    [Fact]
    public void ToolSources_NeverTouchTheUnitOfWork_TheAuditPipeline_OrValidators()
    {
        string[] forbidden = ["IUnitOfWork", "IAuditContext", "IAuditLogger", "IValidator<", "SaveChanges"];

        var offenders = ToolSources()
            .SelectMany(s => forbidden.Where(s.Text.Contains).Select(token => $"{s.Path} ({token})"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Tools write only through McpToolInvoker.Write*Async, which owns the transaction and the audit flush: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void ToolAttributes_SetEveryHintExplicitly()
    {
        // The spec defaults when a hint is unset are destructive and open-world.
        var offenders = ScanToolClasses()
            .SelectMany(c => c.Tools.Select(t => (c.Path, Tool: t)))
            .Where(x => x.Tool.MissingHints.Count > 0)
            .Select(x => $"{x.Path} {x.Tool.Method}: {string.Join(", ", x.Tool.MissingHints)}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"[McpServerTool] sets ReadOnly, Destructive, Idempotent and OpenWorld explicitly: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void WriteTools_CarryAnAuthorizePolicy()
    {
        // [Authorize] + AddAuthorizationFilters() hides the tool from tools/list and rejects calls for read-only roles.
        var offenders = ScanToolClasses()
            .SelectMany(c => c.Tools.Select(t => (c.Path, Tool: t)))
            .Where(x => x.Tool.ReadOnly != true && !x.Tool.HasAuthorizePolicy)
            .Select(x => $"{x.Path} {x.Tool.Method}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Every tool with ReadOnly = false carries [Authorize(Policy = …)]: {string.Join(", ", offenders)}");
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

    // Which invoker frames each handler may run in. A handler the tools start using must be classified here first —
    // an unclassified one fails the rule, so nobody forgets to decide whether it takes a document id.
    private static partial class HandlerRules
    {
        private const string Read = "ReadAsync";
        private const string ReadDocument = "ReadDocumentAsync";
        private const string Write = "WriteAsync";
        private const string WriteDocument = "WriteDocumentAsync";

        // Take a document id and do no access check of their own: on /api the endpoint's document-authorization
        // filter guards them, so over MCP they may run only under the id-authorizing frames. The suggestion
        // handlers belong here — they load by id and mutate with no access check.
        private static readonly HashSet<string> DocumentScoped =
        [
            "GetDocumentHandler", "PatchDocumentHandler", "CreateCommentHandler",
            "ApproveSuggestionHandler", "DeclineSuggestionHandler",
            "ApproveAllSuggestionsHandler", "DeclineAllSuggestionsHandler",
        ];

        // Not a handler, but the same risk: McpDocumentReads reads one document by id and checks no access, so
        // every member runs only under the id-authorizing frames (no Get-name convention applies to it).
        public const string DocumentReads = "McpDocumentReads";

        // Take no document id (lists, tag and party creation).
        private static readonly HashSet<string> Unscoped =
            ["GetDocumentsHandler", "GetTagsHandler", "GetPartiesHandler", "CreateTagHandler", "CreatePartyHandler"];

        // The only handlers that authorize every document they touch themselves, all-or-nothing and before any write
        // (BulkUpdateDocumentsHandler with one EnsureAllDocumentsWritableAsync over the found ids,
        // BulkDeleteDocumentsHandler with a Write check per found document), so plain WriteAsync is enough.
        private static readonly HashSet<string> SelfAuthorizing =
            ["BulkUpdateDocumentsHandler", "BulkDeleteDocumentsHandler"];

        public static string? Check(string handler, string? frame)
        {
            var isGet = handler.StartsWith("Get", StringComparison.Ordinal);
            string[] allowed =
                handler == DocumentReads ? [ReadDocument, WriteDocument]
                : SelfAuthorizing.Contains(handler) ? [Write, WriteDocument]
                : DocumentScoped.Contains(handler) ? (isGet ? [ReadDocument, WriteDocument] : [WriteDocument])
                : Unscoped.Contains(handler) ? (isGet ? [Read, ReadDocument, Write, WriteDocument] : [Write, WriteDocument])
                : [];

            if (allowed.Length == 0)
                return "unclassified handler — add it to McpConventionTests.HandlerRules (does it take a document id?)";
            if (frame is null)
                return $"called outside the invoker; allowed frames: {string.Join(" / ", allowed)}";
            return allowed.Contains(frame) ? null : $"called under {frame}; allowed frames: {string.Join(" / ", allowed)}";
        }
    }

    private sealed record ToolMethod(
        string Method, bool? ReadOnly, IReadOnlyList<string> MissingHints, bool HasAuthorizePolicy);

    // Frame = the innermost invoker method whose work delegate (a lambda, anonymous method or method group argument)
    // contains the call; null = outside every frame, including a call in the invoker's id or ct argument.
    private sealed record HandlerCall(string Path, int Line, string Member, string Handler, string? Frame);

    private sealed record ToolClass(
        string Path, string Name, IReadOnlyList<ToolMethod> Tools, IReadOnlyList<HandlerCall> HandlerCalls);

    /// <summary>
    /// A syntax-only Roslyn scan (no compilation, no semantic model): <c>[McpServerToolType]</c> types (class, record,
    /// struct), their <c>[McpServerTool]</c> methods' hint arguments and <c>[Authorize(Policy = …)]</c>, and every
    /// handler reference in the type's body — a use of a primary-constructor parameter typed <c>*Handler</c> or <c>McpDocumentReads</c>,
    /// or a <c>*Handler</c> name used as a type — with the frames it runs in: only an invoker call's delegate argument
    /// is a frame, since its id and ct arguments are evaluated before it authorizes. Names are matched by text, so a
    /// shadowing local or an aliased type is not told apart; comments and string contents are trivia or tokens, never
    /// names, while an interpolated string's holes are ordinary expressions.
    /// </summary>
    private static class ToolSourceScanner
    {
        private const string HandlerSuffix = "Handler";
        private static readonly string[] InvokerMethods = ["ReadAsync", "ReadDocumentAsync", "WriteAsync", "WriteDocumentAsync"];
        private static readonly string[] Hints = ["ReadOnly", "Destructive", "Idempotent", "OpenWorld"];

        private sealed record Scope(
            TypeDeclarationSyntax Type, string? Invoker, IReadOnlyDictionary<string, string> HandlerParameters);

        public static IEnumerable<ToolClass> Scan(string path, string text) =>
            CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(t => HasAttribute(t.AttributeLists, "McpServerToolType"))
                .Select(t => ScanType(path, t))
                .ToList();

        private static ToolClass ScanType(string path, TypeDeclarationSyntax type)
        {
            var parameters = (type.ParameterList?.Parameters ?? [])
                .Where(p => p.Type is not null)
                .Select(p => (Type: SimpleTypeName(p.Type!), Name: p.Identifier.ValueText))
                .ToList();
            var scope = new Scope(
                type,
                parameters.FirstOrDefault(p => p.Type == "McpToolInvoker").Name,
                parameters
                    .Where(p => p.Type.EndsWith(HandlerSuffix, StringComparison.Ordinal) || p.Type == HandlerRules.DocumentReads)
                    .ToDictionary(p => p.Name, p => p.Type));

            var calls = new List<HandlerCall>();
            foreach (var name in BodyNames(type))
            {
                if (HandlerReferenced(name, scope) is not { } handler)
                    continue;

                var member = OwningMember(name, type);
                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var frames = ResolveFrames(name, scope, []);
                // Empty only for a helper reachable solely through a cycle of helpers: no tool frames it, so outside.
                foreach (var frame in frames.Count == 0 ? [null] : frames)
                    calls.Add(new HandlerCall(path, line, MemberName(member), handler, frame));
            }

            var tools = type.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(IsTool)
                .Select(ReadToolAttributes)
                .ToList();

            return new ToolClass(path, type.Identifier.ValueText, tools, calls);
        }

        // The handler a name refers to: a handler parameter used by name (invoked, passed on, captured), or a
        // *Handler name in a type position (new, typeof, generic argument, static access, local declaration).
        private static string? HandlerReferenced(SimpleNameSyntax name, Scope scope)
        {
            if (IsMemberName(name))
                return null;

            var text = name.Identifier.ValueText;
            if (name is IdentifierNameSyntax && scope.HandlerParameters.TryGetValue(text, out var type))
                return type;

            var invoked = name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name;
            return !invoked && text.Length > HandlerSuffix.Length && text.EndsWith(HandlerSuffix, StringComparison.Ordinal)
                ? text
                : null;
        }

        // The frames a node runs in: the innermost invoker call whose delegate argument contains it, or — inside a
        // non-tool method with no frame of its own — the frames of each of that helper's call sites. Null = outside
        // every frame. visited holds the helpers on the current path (the cycle guard).
        private static List<string?> ResolveFrames(SyntaxNode node, Scope scope, ImmutableHashSet<string> visited)
        {
            var frame = node.AncestorsAndSelf()
                .OfType<ArgumentSyntax>()
                .Where(IsDelegate)
                .Select(argument => FrameKind(argument.Parent?.Parent, scope))
                .FirstOrDefault(kind => kind is not null);
            if (frame is not null)
                return [frame];

            if (OwningMember(node, scope.Type) is not MethodDeclarationSyntax helper || IsTool(helper))
                return [null];

            var name = helper.Identifier.ValueText;
            if (visited.Contains(name))
                return [];

            var callSites = BodyNames(scope.Type)
                .Where(n => n.Identifier.ValueText == name
                            && !helper.Span.Contains(n.Span)
                            && (!IsMemberName(n) || n.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }))
                .ToList();
            if (callSites.Count == 0)
                return [null];

            return callSites.SelectMany(site => ResolveFrames(site, scope, visited.Add(name))).ToList();
        }

        // The work argument's shapes: a lambda or anonymous method (whatever it calls runs inside the frame), or a
        // method group (Helper, this.Helper, field.Method — a name, not invoked). An id or ct argument of that same
        // shape (document_id, input.Id) also counts, as syntax cannot tell a method group from a property; one that
        // computes anything ((await x).Id, Parse(s)) does not.
        private static bool IsDelegate(ArgumentSyntax argument) => argument.Expression
            is AnonymousFunctionExpressionSyntax
            or SimpleNameSyntax
            or MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax or ThisExpressionSyntax, Name: SimpleNameSyntax };

        // invoker.ReadAsync(…) / invoker.WriteDocumentAsync<T>(…) and so on → the method name; anything else → null.
        private static string? FrameKind(SyntaxNode? node, Scope scope) =>
            scope.Invoker is not null
            && node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target, Name: var method },
            }
            && target.Identifier.ValueText == scope.Invoker
            && InvokerMethods.Contains(method.Identifier.ValueText)
                ? method.Identifier.ValueText
                : null;

        private static ToolMethod ReadToolAttributes(MethodDeclarationSyntax method)
        {
            var attributes = method.AttributeLists.SelectMany(l => l.Attributes).ToList();
            var arguments = attributes.First(a => IsNamed(a, "McpServerTool")).ArgumentList?.Arguments ?? [];

            var values = Hints.ToDictionary(
                h => h,
                h => arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == h)?.Expression.Kind() switch
                {
                    SyntaxKind.TrueLiteralExpression => true,
                    SyntaxKind.FalseLiteralExpression => false,
                    _ => (bool?)null,
                });

            var hasPolicy = attributes.Any(a =>
                IsNamed(a, "Authorize")
                && (a.ArgumentList?.Arguments ?? []).Any(x => x.NameEquals?.Name.Identifier.ValueText == "Policy"));

            return new ToolMethod(
                method.Identifier.ValueText,
                values["ReadOnly"],
                Hints.Where(h => values[h] is null).ToList(),
                hasPolicy);
        }

        // Every simple name in the type's members, attributes excluded (a nameof in a [Description] runs nothing).
        private static IEnumerable<SimpleNameSyntax> BodyNames(TypeDeclarationSyntax type) =>
            type.Members.SelectMany(m => m
                .DescendantNodes(n => n is not AttributeListSyntax)
                .OfType<SimpleNameSyntax>());

        // x.Name — the member being accessed, not a reference to a parameter or type of that name.
        private static bool IsMemberName(SimpleNameSyntax name) =>
            name.Parent is MemberAccessExpressionSyntax access && access.Name == name;

        private static MemberDeclarationSyntax? OwningMember(SyntaxNode node, TypeDeclarationSyntax type) =>
            node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m.Parent == type);

        private static string MemberName(MemberDeclarationSyntax? member) => member switch
        {
            MethodDeclarationSyntax m => m.Identifier.ValueText,
            PropertyDeclarationSyntax p => p.Identifier.ValueText,
            ConstructorDeclarationSyntax c => c.Identifier.ValueText,
            BaseFieldDeclarationSyntax f => string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText)),
            _ => "?",
        };

        private static bool IsTool(MethodDeclarationSyntax method) => HasAttribute(method.AttributeLists, "McpServerTool");

        private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
            lists.SelectMany(l => l.Attributes).Any(a => IsNamed(a, name));

        // [Name], [NameAttribute], [Ns.Name] — the last identifier, with or without the Attribute suffix.
        private static bool IsNamed(AttributeSyntax attribute, string name)
        {
            var text = SimpleTypeName(attribute.Name);
            return text == name || text == name + "Attribute";
        }

        // Ns.Type<T>? → Type.
        private static string SimpleTypeName(TypeSyntax type) => type switch
        {
            NullableTypeSyntax n => SimpleTypeName(n.ElementType),
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => type.ToString(),
        };
    }
}
