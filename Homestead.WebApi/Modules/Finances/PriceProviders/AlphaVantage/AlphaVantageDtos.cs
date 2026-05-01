using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

public class AlphaVantageDailyResponse
{
    [JsonPropertyName("Time Series (Daily)")]
    public Dictionary<string, JsonElement>? TimeSeriesDaily { get; set; }

    [JsonPropertyName("Information")]
    public string? Information { get; set; }

    [JsonPropertyName("Note")]
    public string? Note { get; set; }

    [JsonPropertyName("Error Message")]
    public string? ErrorMessage { get; set; }
}

public class AlphaVantageWeeklyResponse
{
    [JsonPropertyName("Weekly Time Series")]
    public Dictionary<string, JsonElement>? TimeSeriesWeekly { get; set; }

    [JsonPropertyName("Information")]
    public string? Information { get; set; }

    [JsonPropertyName("Note")]
    public string? Note { get; set; }

    [JsonPropertyName("Error Message")]
    public string? ErrorMessage { get; set; }
}
