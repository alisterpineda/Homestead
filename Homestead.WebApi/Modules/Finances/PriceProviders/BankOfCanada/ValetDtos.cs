using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

internal class ValetObservationsResponse
{
    [JsonPropertyName("observations")]
    public List<JsonElement> Observations { get; set; } = new();
}

internal record ValetObservationValue
{
    [JsonPropertyName("v")]
    public string? V { get; set; }
}
