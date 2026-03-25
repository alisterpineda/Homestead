using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Common;

public class UnitOfWorkBehavior<TRequest, TResponse>(
    IAuditContext auditContext,
    IAuditLogger auditLogger,
    AppDbContext db)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (auditContext.HasEntries)
        {
            auditContext.FlushTo(auditLogger);
            await db.SaveChangesAsync(cancellationToken);
        }

        return response;
    }
}
