using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayeeMappings;

public class GetPayeeMappingsHandler(AppDbContext db)
    : IRequestHandler<GetPayeeMappingsQuery, IReadOnlyList<PayeeMappingResponse>>
{
    public async Task<IReadOnlyList<PayeeMappingResponse>> Handle(
        GetPayeeMappingsQuery request, CancellationToken ct)
    {
        // Most-confirmed first, then most-recent — the reviewer's mental order (a payee's strongest learned
        // fingerprints on top). No pagination: a single payee's learned descriptions are a small set.
        return await db.PayeeMappings
            .AsNoTracking()
            .Where(m => m.PayeeId == request.PayeeId)
            .OrderByDescending(m => m.TimesConfirmed)
            .ThenByDescending(m => m.UpdatedAt)
            .Select(m => new PayeeMappingResponse(
                m.Id, m.NormalizedKey, m.SampleRawDescription, m.TimesConfirmed, m.UpdatedAt))
            .ToListAsync(ct);
    }
}
