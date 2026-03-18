using System.Text.Json;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Configuration;

public class DbConfigurationProvider(DbConfigurationSource source)
    : ConfigurationProvider, IAppSettingsConfigurationProvider
{
    private const string KeyPrefix = "AppSettings:Modules";

    public override void Load()
    {
        if (source.ServiceProvider is null)
            return;

        try
        {
            using var scope = source.ServiceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var setting in db.AppSettings.AsNoTracking())
            {
                var prefix = $"{KeyPrefix}:{setting.Module}";
                using var doc = JsonDocument.Parse(setting.Value);
                FlattenJson(doc.RootElement, prefix, data);
            }

            Data = data;
        }
        catch (Exception) when (source.ServiceProvider is not null)
        {
            // Gracefully handle edge cases (table not yet created, database unreachable, etc.)
            // POCO defaults cover the gap until the next successful reload.
        }
    }

    public void Reload()
    {
        Load();
        OnReload();
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string?> data)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    FlattenJson(prop.Value, $"{prefix}:{prop.Name}", data);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    FlattenJson(item, $"{prefix}:{i++}", data);
                break;
            case JsonValueKind.String:
                data[prefix] = element.GetString();
                break;
            default:
                data[prefix] = element.GetRawText();
                break;
        }
    }
}
