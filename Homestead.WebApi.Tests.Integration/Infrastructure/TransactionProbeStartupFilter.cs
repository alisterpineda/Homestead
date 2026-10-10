using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Test-only branch that serves the <see cref="TransactionRollbackProbes"/> endpoints. It runs ahead of the
/// app's own pipeline (startup filters wrap it), so it brings its own exception handling. A probe's
/// deliberate <see cref="TransactionRollbackProbes.RollbackProbeException"/> reaches the middleware below only
/// after <c>CommandTransactionFilter</c> has rolled the transaction back, and becomes
/// <see cref="TransactionRollbackProbes.ProbeFailedStatusCode"/>; any other failure is a 500.
/// </summary>
public class TransactionProbeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.MapWhen(
                context => context.Request.Path.StartsWithSegments(TransactionRollbackProbes.RoutePrefix),
                probes =>
                {
                    probes.UseExceptionHandler();
                    probes.Use(async (context, nextMiddleware) =>
                    {
                        try
                        {
                            await nextMiddleware();
                        }
                        catch (TransactionRollbackProbes.RollbackProbeException)
                        {
                            context.Response.StatusCode = TransactionRollbackProbes.ProbeFailedStatusCode;
                        }
                    });
                    probes.UseRouting();
                    probes.UseEndpoints(TransactionRollbackProbes.MapProbeEndpoints);
                });

            next(app);
        };
    }
}
