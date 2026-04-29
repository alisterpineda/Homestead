using System.Globalization;
using System.Text.Json;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

public record ValetObservation(string SeriesName, DateTimeOffset PriceAt, decimal Value);

public class BankOfCanadaValetClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<ValetObservation>> GetObservationsAsync(
        Uri baseUrl,
        IReadOnlyCollection<string> seriesNames,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (seriesNames.Count == 0)
            return Array.Empty<ValetObservation>();

        var seriesPath = string.Join(",", seriesNames);
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var baseStr = baseUrl.ToString();
        if (!baseStr.EndsWith('/'))
            baseStr += "/";

        var url = $"{baseStr}observations/{seriesPath}/json?start_date={fromStr}&end_date={toStr}";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<ValetObservationsResponse>(
            stream, _jsonOptions, ct);

        if (payload is null)
            return Array.Empty<ValetObservation>();

        var results = new List<ValetObservation>();
        foreach (var obsEl in payload.Observations)
        {
            if (!obsEl.TryGetProperty("d", out var dEl) || dEl.ValueKind != JsonValueKind.String)
                continue;
            var dateStr = dEl.GetString();
            if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                continue;

            var priceAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            foreach (var seriesName in seriesNames)
            {
                if (!obsEl.TryGetProperty(seriesName, out var seriesEl)
                    || seriesEl.ValueKind != JsonValueKind.Object)
                    continue;
                if (!seriesEl.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.String)
                    continue;
                var rawValue = vEl.GetString();
                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;
                if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    continue;

                results.Add(new ValetObservation(seriesName, priceAt, value));
            }
        }

        return results;
    }
}
