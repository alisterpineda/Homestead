namespace Homestead.WebApi.Modules.Mcp;

// Turns the "Mcp" section into an McpAvailability, and resolves the scopes the protected-resource metadata
// advertises.
//
// Validation returns a reason rather than throwing (no ValidateOnStart, mirroring AiConfiguration): a broken Mcp
// section must not stop the app from booting — the MCP door is optional, and the build-time OpenAPI generator boots
// the host with no meaningful config. A broken section leaves the endpoint unmapped and logs one Warning.
public static class McpConfiguration
{
    public const string OfflineAccessScope = "offline_access";

    public static readonly string[] BaseScopes = ["openid", "profile", "email"];

    // "Present" = any of the four strings is set. AdditionalScopes alone means nothing without an issuer.
    public static McpAvailability Validate(McpOptions options, bool isDevelopment)
    {
        var present = !string.IsNullOrWhiteSpace(options.Authority)
            || !string.IsNullOrWhiteSpace(options.Audience)
            || !string.IsNullOrWhiteSpace(options.MetadataAddress)
            || !string.IsNullOrWhiteSpace(options.PublicUrl);

        if (!present)
            return McpAvailability.Absent;
        if (!IsAbsoluteHttpUri(options.Authority))
            return McpAvailability.Unavailable("Mcp:Authority must be an absolute URI.");
        if (string.IsNullOrWhiteSpace(options.Audience))
            return McpAvailability.Unavailable("Mcp:Audience is required.");
        if (string.IsNullOrWhiteSpace(options.PublicUrl))
        {
            // Development infers the resource from the request; behind a TLS-terminating proxy the request's
            // scheme/host cannot be trusted to rebuild it byte-for-byte, so production must say what URL users type.
            if (!isDevelopment)
                return McpAvailability.Unavailable(
                    "Mcp:PublicUrl is required outside Development (the public URL of /mcp).");
        }
        else if (!IsAbsoluteHttpUri(options.PublicUrl))
        {
            return McpAvailability.Unavailable("Mcp:PublicUrl must be an absolute URI.");
        }
        if (!string.IsNullOrWhiteSpace(options.MetadataAddress) && !IsAbsoluteHttpUri(options.MetadataAddress))
            return McpAvailability.Unavailable("Mcp:MetadataAddress must be an absolute URI.");

        return McpAvailability.Ready;
    }

    // Base + additional, distinct, in order. offline_access is dropped: a resource server should not advertise it
    // (clients add it when the IdP's discovery lists it); LogMcpAvailability says so when it was configured.
    public static IReadOnlyList<string> ResolveScopes(McpOptions options) =>
        BaseScopes
            .Concat(options.AdditionalScopes.Select(s => s.Trim()).Where(s => s.Length > 0))
            .Where(s => !string.Equals(s, OfflineAccessScope, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // Uri.TryCreate alone accepts "/realms/x" as an absolute file URI on Unix, so require http(s).
    private static bool IsAbsoluteHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
