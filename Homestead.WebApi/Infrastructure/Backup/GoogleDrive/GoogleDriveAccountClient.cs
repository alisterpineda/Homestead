using System.Net.Http.Headers;
using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <param name="StorageLimitBytes">Null means unlimited — not "zero".</param>
public record GoogleDriveAccountInfo(
    string? EmailAddress,
    long? StorageUsedBytes,
    long? StorageLimitBytes);

/// <summary>
/// Reads <c>about.get</c> for a raw access token.
/// </summary>
/// <remarks>
/// Separate from the target's own <c>DriveService</c> because both callers need it <i>before</i> a grant
/// is stored: the connect poll asks "whose account did we just get?" with a token that is not yet on any
/// row, and validate probes with a token it may be about to discard. Building a whole service around a
/// throwaway credential to make one GET is not worth it.
/// </remarks>
public interface IGoogleDriveAccountClient
{
    Task<GoogleDriveAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct);
}

public class GoogleDriveAccountClient(HttpClient httpClient) : IGoogleDriveAccountClient
{
    private const string AboutEndpoint =
        "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress),storageQuota(limit,usage)";

    public async Task<GoogleDriveAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AboutEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
        {
            throw new GoogleDriveAuthException("unauthorized",
                "Google rejected the access token when reading the connected account.");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        string? email = null;
        if (root.TryGetProperty("user", out var user)
            && user.TryGetProperty("emailAddress", out var emailValue))
        {
            email = emailValue.GetString();
        }

        long? usage = null;
        long? limit = null;
        if (root.TryGetProperty("storageQuota", out var quota))
        {
            usage = ReadLong(quota, "usage");
            // Absent on an unlimited account, which is a real state and not zero — so it stays null all
            // the way to the card rather than rendering as "0 bytes free".
            limit = ReadLong(quota, "limit");
        }

        return new GoogleDriveAccountInfo(email, usage, limit);
    }

    // Drive sends 64-bit quota values as JSON strings.
    private static long? ReadLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}
