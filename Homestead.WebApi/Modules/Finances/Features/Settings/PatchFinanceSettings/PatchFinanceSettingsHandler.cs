using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

public class PatchFinanceSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<PatchFinanceSettingsCommand, FinanceSettingsResponse>
{
    public async Task<FinanceSettingsResponse> Handle(
        PatchFinanceSettingsCommand command, CancellationToken ct)
    {
        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.FinancesModule, ct);

        var settings = FinanceSettings.From(entity);

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        command.PatchDocument.ApplyOrThrow(settings);

        if (settings.DisplayCurrencyId is { } currencyId)
        {
            var isCurrency = await db.Commodities
                .AnyAsync(c => c.Id == currencyId && c.Kind == CommodityKind.Currency, ct);
            if (!isCurrency)
                throw new BusinessRuleException("DisplayCurrencyId must reference an existing currency commodity.");
        }

        var newValue = JsonSerializer.Serialize(settings, FieldChange.JsonOptions);

        if (entity is null)
        {
            entity = new AppSetting
            {
                Key = AppSetting.FinancesModule,
                Value = newValue,
                UpdatedByUserId = currentUser.UserId
            };
            db.AppSettings.Add(entity);

            auditLogger.LogCreated("AppSetting", AppSettingSnapshot.KeyToGuid(AppSetting.FinancesModule));
        }
        else
        {
            entity.Value = newValue;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedByUserId = currentUser.UserId;

            var after = AppSettingSnapshot.From(entity);
            var changes = before!.Diff(after);

            if (changes.Count > 0)
                auditLogger.LogUpdated("AppSetting", AppSettingSnapshot.KeyToGuid(AppSetting.FinancesModule),
                    changes);
        }

        await db.SaveChangesAsync(ct);

        return new FinanceSettingsResponse
        {
            DisplayCurrencyId = settings.DisplayCurrencyId
        };
    }
}
