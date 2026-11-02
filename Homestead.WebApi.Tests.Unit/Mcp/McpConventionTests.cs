using System.Text.RegularExpressions;

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
    public void HandlerCalls_RunInsideTheInvokerFrameTheirKindRequires()
    {
        var offenders = FrameProblems(ScanToolClasses())
            .Select(x => $"{x.Call.Path}:{x.Call.Line} {x.Call.Member} → {x.Call.Handler}: {x.Problem}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Handler calls in *Tools.cs run inside McpToolInvoker: Get* handlers under ReadAsync / ReadDocumentAsync / " +
            "Write*Async, every other handler under Write*Async, document-id handlers under ReadDocumentAsync / " +
            "WriteDocumentAsync (only the self-authorizing bulk and suggestion handlers may use plain WriteAsync), " +
            "McpDocumentReads under ReadDocumentAsync / WriteDocumentAsync:\n" +
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

        // The only handlers that authorize every document they touch themselves (EnsureAllDocumentsWritableAsync),
        // so plain WriteAsync is enough.
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

    // Frame = the innermost invoker method whose argument list contains the call; null = outside every frame.
    private sealed record HandlerCall(string Path, int Line, string Member, string Handler, string? Frame);

    private sealed record ToolClass(
        string Path, string Name, IReadOnlyList<ToolMethod> Tools, IReadOnlyList<HandlerCall> HandlerCalls);

    /// <summary>
    /// A pragmatic text scan, not a parser: comments and string/char literals are blanked (lengths kept), then
    /// bracket depth splits the class body into members and finds each invoker call's argument-list extent.
    /// Handler references are the constructor's <c>*Handler</c> and <c>McpDocumentReads</c> parameters and any
    /// <c>*Handler</c> type token in the body.
    /// </summary>
    private static partial class ToolSourceScanner
    {
        private static readonly string[] InvokerMethods = ["ReadAsync", "ReadDocumentAsync", "WriteAsync", "WriteDocumentAsync"];
        private static readonly string[] Hints = ["ReadOnly", "Destructive", "Idempotent", "OpenWorld"];

        [GeneratedRegex(@"\[\s*McpServerToolType\s*\][^{;]*?\bclass\s+(?<name>\w+)")]
        private static partial Regex ClassHeader();

        [GeneratedRegex(@"\bMcpServerTool(Attribute)?\b")]
        private static partial Regex ToolAttribute();

        [GeneratedRegex(@"\bAuthorize(Attribute)?\s*\(\s*Policy\s*=")]
        private static partial Regex AuthorizePolicy();

        [GeneratedRegex(@"\b(?<name>\w+Handler)\b")]
        private static partial Regex HandlerTypeToken();

        [GeneratedRegex(@"\b(?<name>[A-Za-z_]\w*)\s*(<[^(){};]*>)?\s*\(")]
        private static partial Regex MethodNameBeforeParen();

        private sealed record Member(string? Name, int Start, int End, string Attributes, bool IsTool);

        private sealed record Frame(string Kind, int Start, int End);

        public static IEnumerable<ToolClass> Scan(string path, string raw)
        {
            var text = Mask(raw);

            foreach (Match header in ClassHeader().Matches(text))
            {
                var i = header.Index + header.Length;
                while (i < text.Length && text[i] is not ('(' or '{')) i++;

                var ctorParameters = "";
                if (text[i] == '(')
                {
                    var close = MatchClose(text, i);
                    ctorParameters = text[(i + 1)..close];
                    i = close;
                    while (text[i] != '{') i++;
                }

                var bodyStart = i;
                var bodyEnd = MatchClose(text, bodyStart);
                var parameters = ParseParameters(ctorParameters);
                var handlerFields = parameters
                    .Where(p => p.Type.EndsWith("Handler", StringComparison.Ordinal) || p.Type == HandlerRules.DocumentReads)
                    .ToList();
                var invokerName = parameters.FirstOrDefault(p => p.Type == "McpToolInvoker").Name;

                var members = SplitMembers(text, bodyStart + 1, bodyEnd);
                var frames = invokerName is null ? [] : FindFrames(text, bodyStart, bodyEnd, invokerName);

                var references = new List<(int Pos, string Handler)>();
                var body = text[bodyStart..bodyEnd];
                foreach (var (type, name) in handlerFields)
                    references.AddRange(Regex.Matches(body, $@"\b{Regex.Escape(name)}\b")
                        .Select(m => (bodyStart + m.Index, type)));
                references.AddRange(HandlerTypeToken().Matches(body)
                    .Select(m => (bodyStart + m.Index, m.Groups["name"].Value)));

                var calls = new List<HandlerCall>();
                foreach (var (pos, handler) in references)
                {
                    var owner = members.FirstOrDefault(m => pos >= m.Start && pos < m.End);
                    var line = raw[..pos].Count(c => c == '\n') + 1;
                    foreach (var frame in ResolveFrames(pos, text, bodyStart, bodyEnd, members, frames, []))
                        calls.Add(new HandlerCall(path, line, owner?.Name ?? "?", handler, frame));
                }

                var tools = members.Where(m => m.IsTool).Select(m => ReadToolAttributes(m)).ToList();
                yield return new ToolClass(path, header.Groups["name"].Value, tools, calls);
            }
        }

        // The frames a reference at pos runs in: its innermost enclosing invoker call, or — inside a non-tool helper
        // with no frame of its own — the frames of each of the helper's call sites. Null = outside every frame.
        private static List<string?> ResolveFrames(
            int pos, string text, int bodyStart, int bodyEnd, List<Member> members, List<Frame> frames,
            HashSet<string> visited)
        {
            var frame = frames.Where(f => pos > f.Start && pos < f.End).MinBy(f => f.End - f.Start);
            if (frame is not null)
                return [frame.Kind];

            var owner = members.FirstOrDefault(m => pos >= m.Start && pos < m.End);
            if (owner is null || owner.IsTool || owner.Name is null)
                return [null];
            if (!visited.Add(owner.Name))
                return [];

            var callSites = Regex.Matches(text[bodyStart..bodyEnd], $@"\b{Regex.Escape(owner.Name)}\s*(<[^(){{}};]*>)?\s*\(")
                .Select(m => bodyStart + m.Index)
                .Where(p => p < owner.Start || p >= owner.End)
                .ToList();
            if (callSites.Count == 0)
                return [null];

            return callSites
                .SelectMany(p => ResolveFrames(p, text, bodyStart, bodyEnd, members, frames, visited))
                .ToList();
        }

        private static ToolMethod ReadToolAttributes(Member member)
        {
            var attribute = ToolAttribute().Match(member.Attributes);
            var i = attribute.Index + attribute.Length;
            while (i < member.Attributes.Length && char.IsWhiteSpace(member.Attributes[i])) i++;
            var arguments = i < member.Attributes.Length && member.Attributes[i] == '('
                ? member.Attributes[(i + 1)..MatchClose(member.Attributes, i)]
                : "";

            var values = Hints.ToDictionary(
                h => h,
                h => Regex.Match(arguments, $@"\b{h}\s*=\s*(?<value>true|false)\b") is { Success: true } m
                    ? m.Groups["value"].Value == "true"
                    : (bool?)null);

            return new ToolMethod(
                member.Name ?? "?",
                values["ReadOnly"],
                Hints.Where(h => values[h] is null).ToList(),
                AuthorizePolicy().IsMatch(member.Attributes));
        }

        private static List<Frame> FindFrames(string text, int start, int end, string invokerName)
        {
            var pattern = $@"\b{Regex.Escape(invokerName)}\s*\.\s*(?<kind>{string.Join("|", InvokerMethods)})\b\s*(<[^(){{}};]*>)?\s*\(";
            return Regex.Matches(text[start..end], pattern)
                .Select(m =>
                {
                    var open = start + m.Index + m.Length - 1;
                    return new Frame(m.Groups["kind"].Value, open, MatchClose(text, open));
                })
                .ToList();
        }

        // A member ends at a ';' or a closing '}' at depth zero (so an expression body's lambda braces, nested
        // inside the invoker call's parentheses, never end it).
        private static List<Member> SplitMembers(string text, int start, int end)
        {
            var members = new List<Member>();
            int paren = 0, brace = 0, bracket = 0, memberStart = start;

            for (var i = start; i < end; i++)
            {
                switch (text[i])
                {
                    case '(': paren++; break;
                    case ')': paren--; break;
                    case '[': bracket++; break;
                    case ']': bracket--; break;
                    case '{': brace++; break;
                    case '}':
                        brace--;
                        if (brace == 0 && paren == 0 && bracket == 0) Close(i + 1);
                        break;
                    case ';':
                        if (brace == 0 && paren == 0 && bracket == 0) Close(i + 1);
                        break;
                }
            }

            return members;

            void Close(int memberEnd)
            {
                var segment = text[memberStart..memberEnd];
                var j = 0;
                while (j < segment.Length && char.IsWhiteSpace(segment[j])) j++;

                var attributes = new List<string>();
                while (j < segment.Length && segment[j] == '[')
                {
                    var close = MatchClose(segment, j);
                    attributes.Add(segment[(j + 1)..close]);
                    j = close + 1;
                    while (j < segment.Length && char.IsWhiteSpace(segment[j])) j++;
                }

                if (j < segment.Length)
                {
                    var signature = segment[j..];
                    var name = MethodNameBeforeParen().Match(signature) is { Success: true } m
                               && !signature[..m.Index].Contains('=')
                        ? m.Groups["name"].Value
                        : null;
                    var attributeText = string.Join("\n", attributes);
                    members.Add(new Member(name, memberStart, memberEnd, attributeText, ToolAttribute().IsMatch(attributeText)));
                }

                memberStart = memberEnd;
            }
        }

        private static List<(string Type, string Name)> ParseParameters(string parameters)
        {
            var result = new List<(string, string)>();
            int depth = 0, start = 0;
            for (var i = 0; i <= parameters.Length; i++)
            {
                if (i < parameters.Length)
                {
                    var c = parameters[i];
                    if (c is '(' or '<' or '[') depth++;
                    else if (c is ')' or '>' or ']') depth--;
                    if (c != ',' || depth != 0) continue;
                }

                var parameter = Regex.Replace(parameters[start..i], @"\[[^\]]*\]", "").Split('=')[0].Trim();
                if (Regex.Match(parameter, @"^(?<type>.+?)\s+@?(?<name>\w+)$", RegexOptions.Singleline) is { Success: true } m)
                {
                    var type = Regex.Replace(m.Groups["type"].Value, @"<.*>|\?", "").Trim();
                    result.Add((type[(type.LastIndexOf('.') + 1)..], m.Groups["name"].Value));
                }
                start = i + 1;
            }
            return result;
        }

        // The index of the bracket closing the one at open ('(', '[' or '{'), counting only that bracket kind.
        private static int MatchClose(string text, int open)
        {
            var (opener, closer) = text[open] switch
            {
                '(' => ('(', ')'),
                '[' => ('[', ']'),
                '{' => ('{', '}'),
                _ => throw new ArgumentException($"No bracket at {open}."),
            };

            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == opener) depth++;
                else if (text[i] == closer && --depth == 0) return i;
            }
            throw new InvalidOperationException($"Unbalanced '{opener}' at {open}.");
        }

        // Comments and string/char literal contents become spaces (newlines kept), so brackets, handler names and
        // hint text inside them are invisible to the scan and every index still points into the raw source.
        private static string Mask(string source)
        {
            var s = source.ToCharArray();
            var i = 0;
            while (i < s.Length)
            {
                if (s[i] == '/' && At(i + 1) == '/')
                {
                    while (i < s.Length && s[i] != '\n') Blank(i++);
                }
                else if (s[i] == '/' && At(i + 1) == '*')
                {
                    Blank(i++); Blank(i++);
                    while (i < s.Length && !(s[i] == '*' && At(i + 1) == '/')) Blank(i++);
                    if (i < s.Length) { Blank(i++); Blank(i++); }
                }
                else if (s[i] == '"' && At(i + 1) == '"' && At(i + 2) == '"')
                {
                    var quotes = 0;
                    while (At(i) == '"') { quotes++; i++; }
                    while (i < s.Length)
                    {
                        var run = 0;
                        while (At(i + run) == '"') run++;
                        if (run >= quotes) { i += run; break; }
                        Blank(i++);
                    }
                }
                else if (s[i] == '"')
                {
                    var verbatim = i > 0 && (s[i - 1] == '@' || (s[i - 1] == '$' && i > 1 && s[i - 2] == '@'));
                    i++;
                    while (i < s.Length)
                    {
                        if (verbatim && s[i] == '"' && At(i + 1) == '"') { Blank(i++); Blank(i++); continue; }
                        if (!verbatim && s[i] == '\\') { Blank(i++); if (i < s.Length) Blank(i++); continue; }
                        if (s[i] == '"') { i++; break; }
                        Blank(i++);
                    }
                }
                else if (s[i] == '\'')
                {
                    i++;
                    if (At(i) == '\\') { Blank(i++); if (i < s.Length) Blank(i++); }
                    else if (i < s.Length) Blank(i++);
                    while (i < s.Length && s[i] != '\'') Blank(i++);
                    i++;
                }
                else
                {
                    i++;
                }
            }
            return new string(s);

            char At(int index) => index < s.Length ? s[index] : '\0';
            void Blank(int index) { if (s[index] != '\n') s[index] = ' '; }
        }
    }
}
