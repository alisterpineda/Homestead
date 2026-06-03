using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public class PatchPriceSourceHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    IServiceProvider serviceProvider)
    : IRequestHandler<PatchPriceSourceCommand, PatchPriceSourceResponse>
{
    public async Task<PatchPriceSourceResponse> Handle(PatchPriceSourceCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("configured");

        var entity = await db.CommodityPriceSourceSettings
            .FirstOrDefaultAsync(s => s.Source == request.Source, ct)
            ?? throw new EntityNotFoundException("Price source settings not found.");

        var schema = serviceProvider.GetKeyedService<IPriceProvider>(request.Source)?.ConfigSchema
            ?? PriceProviderConfigSchema.Empty;
        var secretKeys = schema.Fields
            .Where(f => f.Type == PriceProviderConfigFieldType.Secret)
            .Select(f => f.Key)
            .ToList();
        var before = CommodityPriceSourceSettingsSnapshot.From(entity, secretKeys);

        if (request.IsEnabled is { } isEnabled)
            entity.IsEnabled = isEnabled;

        if (request.Config is not null)
        {
            var merged = JsonSecretConfig.Merge(request.Config, entity.Config, secretKeys);
            ValidateConfig(schema, request.Source, merged);
            entity.Config = merged;
        }

        if (request.PairAllowList is not null)
        {
            // Persist the normalized (trimmed + de-duplicated) list, not the raw request — a
            // stored entry with surrounding whitespace or a duplicate would otherwise pass
            // validation here yet silently fail to match in the importer's symbol lookup.
            var normalized = await ValidatePairAllowListAsync(request.PairAllowList, ct);
            entity.PairAllowList = JsonSerializer.Serialize(normalized);
        }

        var now = DateTimeOffset.UtcNow;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = currentUser.UserId;

        var after = CommodityPriceSourceSettingsSnapshot.From(entity, secretKeys);
        var changes = before.Diff(after);
        if (changes.Count > 0)
        {
            audit.Track(() => changes,
                "CommodityPriceSourceSettings",
                CommodityPriceSourceSettingsSnapshot.SourceToGuid(entity.Source));
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new PatchPriceSourceResponse(entity.Source, entity.IsEnabled);
    }

    private static void ValidateConfig(PriceProviderConfigSchema schema, CommodityPriceSource source, string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return;

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(configJson).RootElement;
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("Config must be valid JSON.");
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new BusinessRuleException("Config must be a JSON object.");

        if (schema.Fields.Count == 0)
        {
            using var enumerator = root.EnumerateObject();
            if (enumerator.MoveNext())
                throw new BusinessRuleException(
                    $"Price source '{source}' does not accept any configuration keys.");
            return;
        }

        var allowedKeys = schema.Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            if (!allowedKeys.Contains(prop.Name))
                throw new BusinessRuleException($"Unknown config key '{prop.Name}'.");
        }
        foreach (var field in schema.Fields)
        {
            if (!field.IsRequired) continue;
            if (!root.TryGetProperty(field.Key, out var value)
                || value.ValueKind == JsonValueKind.Null
                || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
                throw new BusinessRuleException($"Config field '{field.Key}' is required.");
        }
    }

    // Validates the requested allow-list and returns the normalized (trimmed + de-duplicated)
    // symbols the caller should persist.
    private async Task<List<string>> ValidatePairAllowListAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var distinct = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0) return distinct;

        // Resolve against all commodities (currencies and securities) — the importer matches
        // allow-list symbols against every held/known commodity and then filters by provider
        // coverage, so a watched security like VEQT is valid here too.
        var resolved = await db.Commodities
            .Where(c => distinct.Contains(c.Symbol))
            .Select(c => c.Symbol)
            .ToListAsync(ct);

        var resolvedSet = resolved.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = distinct.Where(s => !resolvedSet.Contains(s)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException(
                $"Pair allow-list contains unknown symbols: {string.Join(", ", missing)}.");

        return distinct;
    }
}
