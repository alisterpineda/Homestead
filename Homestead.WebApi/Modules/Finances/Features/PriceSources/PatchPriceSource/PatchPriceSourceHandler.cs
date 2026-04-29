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
    IAuditLogger auditLogger,
    IServiceProvider serviceProvider)
    : IRequestHandler<PatchPriceSourceCommand, PatchPriceSourceResponse>
{
    public async Task<PatchPriceSourceResponse> Handle(PatchPriceSourceCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("configured");

        var entity = await db.CommodityPriceSourceSettings
            .FirstOrDefaultAsync(s => s.Source == request.Source, ct)
            ?? throw new EntityNotFoundException("Price source settings not found.");

        var before = CommodityPriceSourceSettingsSnapshot.From(entity);

        if (request.IsEnabled is { } isEnabled)
            entity.IsEnabled = isEnabled;

        if (request.Config is not null)
        {
            ValidateConfig(request.Source, request.Config);
            entity.Config = request.Config;
        }

        if (request.PairAllowList is not null)
        {
            await ValidatePairAllowListAsync(request.PairAllowList, ct);
            entity.PairAllowList = JsonSerializer.Serialize(request.PairAllowList);
        }

        var now = DateTimeOffset.UtcNow;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = currentUser.UserId;

        var after = CommodityPriceSourceSettingsSnapshot.From(entity);
        var changes = before.Diff(after);
        if (changes.Count > 0)
        {
            auditLogger.LogUpdated(
                "CommodityPriceSourceSettings",
                CommodityPriceSourceSettingsSnapshot.SourceToGuid(entity.Source),
                changes);
        }

        await db.SaveChangesAsync(ct);

        return new PatchPriceSourceResponse(entity.Source, entity.IsEnabled);
    }

    private void ValidateConfig(CommodityPriceSource source, string configJson)
    {
        var provider = serviceProvider.GetKeyedService<IPriceProvider>(source);
        var schema = provider?.ConfigSchema ?? PriceProviderConfigSchema.Empty;

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

    private async Task ValidatePairAllowListAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        if (symbols.Count == 0) return;
        var distinct = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = await db.Commodities
            .Where(c => c.Kind == CommodityKind.Currency && distinct.Contains(c.Symbol))
            .Select(c => c.Symbol)
            .ToListAsync(ct);

        var resolvedSet = resolved.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = distinct.Where(s => !resolvedSet.Contains(s)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException(
                $"Pair allow-list contains unknown currency symbols: {string.Join(", ", missing)}.");
    }
}
