using System.Net.Http.Headers;
using System.Text;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>Raw JSON-RPC requests for asserting on the HTTP layer of <c>/mcp</c> (status, headers, problem body).</summary>
internal static class McpRequests
{
    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"homestead-tests","version":"1.0"}}}""";

    /// <summary>A well-formed MCP <c>initialize</c> POST, so only authentication/authorization can refuse it.</summary>
    public static HttpRequestMessage Initialize()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpClientHelper.EndpointPath)
        {
            Content = new StringContent(InitializeBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }
}
