using System.Reflection;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Homestead.WebApi.Modules.Mcp;

// The MCP server: opt-in by the "Mcp" section. Program.cs calls AddHomesteadMcp() and MapHomesteadMcp() and needs no
// SDK using (McpConventionTests keeps SDK types in this folder).
public static class McpServiceExtensions
{
    public const string EndpointPath = "/mcp";

    // How tool arguments are read and tool results written: the SDK's defaults (its type metadata, nulls omitted)
    // with snake_case property names, so result records stay plain C# and the wire matches the parameter names
    // (which the SDK takes verbatim from the C# parameters — hence snake_case parameters in the tool classes).
    public static readonly JsonSerializerOptions ToolJsonOptions = new(McpJsonUtilities.DefaultOptions)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Called right after AddAppAuthentication(): the bearer scheme and policy extend the cookie setup.
    public static WebApplicationBuilder AddHomesteadMcp(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
        var isDevelopment = builder.Environment.IsDevelopment();
        var availability = McpConfiguration.Validate(options, isDevelopment);

        builder.Services.AddSingleton(options);
        builder.Services.AddKeyedSingleton(McpOptions.SectionName, availability);

        if (!availability.IsConfigured)
            return builder;

        builder.Services.AddMcpAuthentication(options, isDevelopment);

        // The tools' shared frame and lookups (tool classes themselves are never registered). Only when configured:
        // nothing else consumes them.
        builder.Services.AddScoped<McpToolInvoker>();
        builder.Services.AddScoped<TagPathSnapper>();
        builder.Services.AddScoped<PartyNameResolver>();
        builder.Services.AddScoped<McpDocumentReads>();

        // Tool classes are discovered here and instantiated per call from the request's services; a DI
        // registration of a tool class is ignored. AddAuthorizationFilters honours [Authorize] on tool methods
        // (hides them from tools/list and rejects calls for callers who fail the policy). The input check runs after
        // it, so a caller the policy rejects never sees an argument message.
        builder.Services.AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "homestead", Version = InformationalVersion() };
                o.ServerInstructions = McpServerInstructions.Text;
            })
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithHomesteadTools(typeof(Program).Assembly)
            .AddAuthorizationFilters()
            .WithRequestFilters(f => f.AddCallToolFilter(CheckArguments));

        return builder;
    }

    // What WithToolsFromAssembly does — every [McpServerTool] method of every [McpServerToolType] class, instantiated
    // per call from the request's services — plus a corrected input schema (McpToolSchemas), which the SDK's own
    // registration offers no hook for.
    private static IMcpServerBuilder WithHomesteadTools(this IMcpServerBuilder builder, Assembly assembly)
    {
        var toolTypes = assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);
        foreach (var toolType in toolTypes)
        {
            var methods = toolType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);
            foreach (var method in methods)
            {
                builder.Services.AddSingleton(services =>
                {
                    var options = new McpServerToolCreateOptions { Services = services, SerializerOptions = ToolJsonOptions };
                    var tool = method.IsStatic
                        ? McpServerTool.Create(method, target: null, options)
                        : McpServerTool.Create(method, request => CreateToolInstance(request, toolType), options);
                    tool.ProtocolTool.InputSchema = McpToolSchemas.TightenArrayItems(tool.ProtocolTool.InputSchema);
                    return tool;
                });
            }
        }

        return builder;
    }

    private static object CreateToolInstance(RequestContext<CallToolRequestParams> request, Type toolType) =>
        ActivatorUtilities.CreateInstance(
            request.Services ?? request.Server.Services
            ?? throw new InvalidOperationException("No service provider to create the tool from."), toolType);

    // The call-tool filter: arguments that do not fit the tool's input schema are answered with a message naming
    // them (McpInputCheck), in the same error shape the SDK gives a tool's own McpException, instead of the SDK's
    // bare "An error occurred invoking '…'." from a failed bind.
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> CheckArguments(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (request, ct) =>
        {
            if (request.MatchedPrimitive is McpServerTool tool)
            {
                var problems = McpInputCheck.Check(tool.ProtocolTool.InputSchema, request.Params?.Arguments);
                if (problems.Count > 0)
                {
                    return ValueTask.FromResult(new CallToolResult
                    {
                        IsError = true,
                        Content =
                        [
                            new TextContentBlock
                            {
                                Text = $"An error occurred invoking '{tool.ProtocolTool.Name}': Invalid input: " +
                                       string.Join("\n", problems),
                            },
                        ],
                    });
                }
            }

            return next(request, ct);
        };

    // Called after the MapHub line.
    public static WebApplication MapHomesteadMcp(this WebApplication app)
    {
        var availability = app.Services.GetRequiredKeyedService<FeatureAvailability>(McpOptions.SectionName);
        if (availability.IsConfigured)
        {
            app.MapMcp(EndpointPath)
                .RequireAuthorization(AuthPolicies.McpActiveUser)
                .ExcludeFromDescription();
        }
        else
        {
            // Not the SPA's index.html: a client probing a disabled server gets a plain 404.
            app.MapMethods(EndpointPath, [HttpMethods.Get, HttpMethods.Post, HttpMethods.Delete], () => Results.NotFound())
                .ExcludeFromDescription()
                .AllowAnonymous();
        }

        // Probes for other well-known documents must not fall through to the SPA fallback (200 + HTML). The MCP
        // handler answers /.well-known/oauth-protected-resource/mcp inside UseAuthentication, before endpoints, so
        // this catch-all does not shadow it.
        app.MapGet("/.well-known/{**rest}", () => Results.NotFound())
            .ExcludeFromDescription()
            .AllowAnonymous();

        app.Services.LogMcpAvailability();
        return app;
    }

    public static void LogMcpAvailability(this IServiceProvider services)
    {
        var availability = services.GetRequiredKeyedService<FeatureAvailability>(McpOptions.SectionName);
        var options = services.GetRequiredService<McpOptions>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpServiceExtensions));

        if (!availability.IsPresent)
        {
            logger.LogInformation("MCP server disabled: set Mcp:Authority, Mcp:Audience and Mcp:PublicUrl");
            return;
        }

        if (!availability.IsConfigured)
        {
            logger.LogWarning("MCP server is configured but unavailable: {Reason}", availability.Reason);
            return;
        }

        logger.LogInformation(
            "MCP server enabled at {Path}: audience {Audience}, issuer {Issuer}, resource {Resource}",
            EndpointPath, options.Audience, options.Authority,
            string.IsNullOrWhiteSpace(options.PublicUrl) ? "(inferred from the request)" : options.PublicUrl);

        if (options.AdditionalScopes.Any(s =>
                string.Equals(s.Trim(), McpConfiguration.OfflineAccessScope, StringComparison.Ordinal)))
            logger.LogInformation(
                "Mcp:AdditionalScopes lists {Scope}; it is not advertised — clients request it themselves when the IdP offers it",
                McpConfiguration.OfflineAccessScope);
    }

    private static string InformationalVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
