using System.Text.Json;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

public class AlphaVantageClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<AlphaVantageDailyResponse?> GetDailyTimeSeriesAsync(
        Uri baseUrl,
        string symbol,
        string apiKey,
        string outputsize,
        CancellationToken ct)
    {
        var baseStr = baseUrl.ToString();
        var separator = baseStr.Contains('?') ? '&' : '?';
        var url = $"{baseStr}{separator}function=TIME_SERIES_DAILY"
            + $"&symbol={Uri.EscapeDataString(symbol)}"
            + $"&outputsize={Uri.EscapeDataString(outputsize)}"
            + $"&apikey={Uri.EscapeDataString(apiKey)}";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<AlphaVantageDailyResponse>(stream, _jsonOptions, ct);
    }

    public async Task<AlphaVantageWeeklyResponse?> GetWeeklyTimeSeriesAsync(
        Uri baseUrl,
        string symbol,
        string apiKey,
        CancellationToken ct)
    {
        var baseStr = baseUrl.ToString();
        var separator = baseStr.Contains('?') ? '&' : '?';
        var url = $"{baseStr}{separator}function=TIME_SERIES_WEEKLY"
            + $"&symbol={Uri.EscapeDataString(symbol)}"
            + $"&apikey={Uri.EscapeDataString(apiKey)}";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<AlphaVantageWeeklyResponse>(stream, _jsonOptions, ct);
    }
}
