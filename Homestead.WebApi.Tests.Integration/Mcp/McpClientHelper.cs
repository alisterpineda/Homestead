using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>Connects a real MCP client to the in-process server over Streamable HTTP, authenticated by a bearer token.</summary>
public static class McpClientHelper
{
    public const string EndpointPath = "/mcp";

    /// <summary>
    /// An <see cref="HttpClient"/> on the test server carrying <paramref name="token"/> as its bearer — for raw
    /// requests against <c>/mcp</c> or to show the token is ignored elsewhere.
    /// </summary>
    public static HttpClient CreateBearerClient(WebApplicationFactory<Program> factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>
    /// Runs the MCP initialize handshake and returns the connected client. Dispose the client (it owns the
    /// transport; the transport does not own the HttpClient, which lives as long as the factory).
    /// </summary>
    public static async Task<McpClient> ConnectAsync(
        WebApplicationFactory<Program> factory, string token, CancellationToken ct = default)
    {
        var http = CreateBearerClient(factory, token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, EndpointPath),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}
