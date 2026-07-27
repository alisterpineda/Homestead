using System.Net;
using System.Text;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// A scripted HTTP stack for the Google clients, modelled on the AlphaVantage fake.
/// </summary>
/// <remarks>
/// Records the form bodies as well as the URLs, because several of these tests are about <i>what was
/// sent</i> — the grant type, the scope — and one is about what was <b>not</b> logged.
/// </remarks>
public class FakeGoogleHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _queued = new();

    public List<string> RequestUrls { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public int CallCount => RequestUrls.Count;

    /// <summary>Served once every queued response is exhausted.</summary>
    public (HttpStatusCode Status, string Body) Fallback { get; set; } = (HttpStatusCode.OK, "{}");

    /// <summary>Thrown instead of responding, for the transient-failure cases.</summary>
    public Exception? Throw { get; set; }

    public void Queue(HttpStatusCode status, string body) => _queued.Enqueue((status, body));

    public void QueueOk(string body) => Queue(HttpStatusCode.OK, body);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
        RequestBodies.Add(
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        if (Throw is { } failure)
            throw failure;

        var (status, body) = _queued.Count > 0 ? _queued.Dequeue() : Fallback;

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
