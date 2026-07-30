using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// A tiny stateful stand-in for Drive's folder API, injected as Google's own
/// <see cref="Google.Apis.Http.IHttpClientFactory"/> so the real <c>GoogleDriveBackupTarget</c> — the real
/// SDK, the real request shapes — runs without a network.
/// </summary>
/// <remarks>
/// <para>
/// Stateful where the unit suite's namesake is deliberately dumb, and for one reason: reconnect-cheapness
/// is a property of the mirror, not of a request. A disconnect drops the folder map but keeps the copy
/// records, so a reconnect has to re-find the SAME folder ids by name along the pinned path. A fake that
/// minted a fresh id per lookup could not tell that working from broken.
/// </para>
/// <para>
/// Folders only. Uploads are not exercised here (the mirror suite runs against the local target), so
/// anything else gets a benign default.
/// </para>
/// </remarks>
public sealed class FakeDriveHandler : HttpMessageHandler
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>(parent id, name) → folder id. The whole point: a name lookup is stable across a reconnect.</summary>
    private readonly Dictionary<(string Parent, string Name), string> _folders = new();

    private int _counter;

    /// <summary>Every folder name the target has actually created.</summary>
    public List<string> Created { get; } = [];

    public void Reset()
    {
        _folders.Clear();
        Created.Clear();
        _counter = 0;
    }

    /// <summary>The id a folder was created with, or null when the fake has never seen it.</summary>
    public string? FolderId(string parentId, string name) =>
        _folders.GetValueOrDefault((parentId, name));

    public static string WebViewLinkFor(string folderId) =>
        $"https://drive.google.com/drive/folders/{folderId}";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (request.Method == HttpMethod.Get && path.EndsWith("/drive/v3/files", StringComparison.Ordinal))
            return ListFolders(request);

        if (request.Method == HttpMethod.Post && path.EndsWith("/drive/v3/files", StringComparison.Ordinal))
            return await CreateFolderAsync(request, cancellationToken);

        return Json(HttpStatusCode.OK, """{"files":[],"id":"default-id"}""");
    }

    private HttpResponseMessage ListFolders(HttpRequestMessage request)
    {
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty).Replace('+', ' ');

        var name = Match(query, @"name = '([^']*)'");
        var parent = Match(query, @"'([^']*)' in parents");

        if (name is null || parent is null)
            return Json(HttpStatusCode.OK, """{"files":[]}""");

        if (!_folders.TryGetValue((parent, name), out var id))
            return Json(HttpStatusCode.OK, """{"files":[]}""");

        return Json(HttpStatusCode.OK,
            $$"""{"files":[{"id":"{{id}}","webViewLink":"{{WebViewLinkFor(id)}}"}]}""");
    }

    private async Task<HttpResponseMessage> CreateFolderAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var body = await ReadSafelyAsync(request, ct);

        string? name = null;
        string? parent = null;
        var isFolder = false;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("name", out var nameElement))
                name = nameElement.GetString();

            if (root.TryGetProperty("mimeType", out var mimeElement))
                isFolder = mimeElement.GetString() == FolderMimeType;

            if (root.TryGetProperty("parents", out var parents) && parents.GetArrayLength() > 0)
                parent = parents[0].GetString();
        }
        catch (JsonException)
        {
            // Not a metadata create; fall through to the default below.
        }

        if (name is null || parent is null || !isFolder)
            return Json(HttpStatusCode.OK, """{"id":"default-id"}""");

        var id = $"folder-{++_counter}";
        _folders[(parent, name)] = id;
        Created.Add(name);

        return Json(HttpStatusCode.OK,
            $$"""{"id":"{{id}}","webViewLink":"{{WebViewLinkFor(id)}}"}""");
    }

    private static string? Match(string input, string pattern) =>
        Regex.Match(input, pattern) is { Success: true } match ? match.Groups[1].Value : null;

    // The SDK gzips request bodies, so the metadata JSON has to be decompressed before it can be read.
    private static async Task<string> ReadSafelyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null)
            return string.Empty;

        try
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);

            if (!request.Content.Headers.ContentEncoding.Contains("gzip"))
                return Encoding.UTF8.GetString(bytes);

            using var compressed = new MemoryStream(bytes);
            await using var gzip = new System.IO.Compression.GZipStream(
                compressed, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            return await reader.ReadToEndAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
