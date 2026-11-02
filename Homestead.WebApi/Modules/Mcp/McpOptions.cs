namespace Homestead.WebApi.Modules.Mcp;

// The "Mcp" section: the bearer door for Claude clients. Opt-in — with no section the endpoint is not mapped and
// /mcp answers 404. Bound from stock Mcp__* env vars; validated by McpConfiguration into an McpAvailability (no
// ValidateOnStart, mirroring AiOptions).
public class McpOptions
{
    public const string SectionName = "Mcp";

    // Required: the IdP issuer, copied verbatim from its discovery document (Authentik's ends in '/'). Advertised
    // byte-for-byte as the protected-resource metadata's authorization server.
    public string? Authority { get; set; }

    // Required: the MCP OAuth client id — the access token's `aud`.
    public string? Audience { get; set; }

    // Optional: an explicit discovery-document URL when it cannot be derived from Authority.
    public string? MetadataAddress { get; set; }

    // The canonical public URL of the /mcp resource (RFC 9728 `resource`). Required outside Development: behind a
    // TLS-terminating proxy the request's scheme/host cannot be trusted to rebuild the URL the user typed, and
    // clients reject a resource that is not byte-identical to it. (The proxy must still preserve Host: the SDK's
    // 401 challenge points at the metadata document on the request's host.)
    public string? PublicUrl { get; set; }

    // Appended to the fixed base ["openid","profile","email"]. offline_access is never advertised (clients add it
    // themselves when the IdP lists it) and is dropped if listed here. Empty by default: the binder appends to a
    // pre-filled list.
    public List<string> AdditionalScopes { get; set; } = [];
}

// Absent config, misconfigured config and "ready" are told apart at startup: absent logs one Information line,
// misconfigured one Warning with the reason. Reason is set only when IsPresent && !IsConfigured.
public sealed record McpAvailability(bool IsConfigured, bool IsPresent, string? Reason)
{
    public static readonly McpAvailability Absent = new(false, false, null);
    public static readonly McpAvailability Ready = new(true, true, null);

    public static McpAvailability Unavailable(string reason) => new(false, true, reason);
}
