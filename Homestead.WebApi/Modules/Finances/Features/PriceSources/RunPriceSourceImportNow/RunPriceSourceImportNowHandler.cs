using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;

public class RunPriceSourceImportNowHandler(
    AppDbContext db,
    IJobQueue queue,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<RunPriceSourceImportNowCommand, RunPriceSourceImportNowResponse>
{
    public async Task<RunPriceSourceImportNowResponse> Handle(
        RunPriceSourceImportNowCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("imported");

        var settings = await db.CommodityPriceSourceSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Source == request.Source, ct);
        if (settings is null || !settings.IsEnabled)
            throw new BusinessRuleException(
                $"Price source '{request.Source}' is disabled.");

        var payload = new ImportCommodityPricesPayload(
            request.Source,
            request.FromAt,
            request.ToAt,
            request.CommodityIds);

        var jobId = await queue.EnqueueAsync(ProcessingJobType.ImportCommodityPrices, opts =>
        {
            opts.Payload = JsonSerializer.Serialize(payload);
            opts.CreatedByUserId = currentUser.UserId;
        }, ct);

        return new RunPriceSourceImportNowResponse(jobId);
    }
}
