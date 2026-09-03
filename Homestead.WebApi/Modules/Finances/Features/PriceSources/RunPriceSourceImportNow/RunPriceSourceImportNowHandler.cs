using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Processing;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;

public class RunPriceSourceImportNowHandler(
    IJobQueue queue,
    ICurrentUserAccessor currentUser,
    IPriceSourceRegistry sources)
    : IRequestHandler<RunPriceSourceImportNowCommand, RunPriceSourceImportNowResponse>
{
    public async Task<RunPriceSourceImportNowResponse> Handle(
        RunPriceSourceImportNowCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("imported");

        // Fail fast with exactly the reason the job would have recorded anyway.
        if (PriceSourceConfiguration.ImportBlocker(sources.Get(request.Source), request.Source) is { } blocked)
            throw new BusinessRuleException(blocked.Message);

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
