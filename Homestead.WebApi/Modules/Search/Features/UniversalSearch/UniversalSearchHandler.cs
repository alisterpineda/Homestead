using Homestead.WebApi.Common.Search;
using MediatR;

namespace Homestead.WebApi.Modules.Search.Features.UniversalSearch;

public class UniversalSearchHandler(IEnumerable<ISearchSource> sources, ILogger<UniversalSearchHandler> logger)
    : IRequestHandler<UniversalSearchQuery, SearchResponse>
{
    private const int MinQueryLength = 2;
    private const int DefaultLimit = 6;
    private const int MaxLimit = 20;

    // Per-source soft timeout. A source that overruns is dropped from the results (partial response)
    // so one slow/broken source can't stall the palette.
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(3);

    public async Task<SearchResponse> Handle(UniversalSearchQuery request, CancellationToken ct)
    {
        var query = request.Q?.Trim() ?? string.Empty;

        // Min-length guard — too short to be meaningful; skip the fan-out entirely.
        if (query.Length < MinQueryLength)
            return new SearchResponse(query, []);

        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);

        // Sequential fan-out in v1: AppDbContext is scoped and not thread-safe, so sources sharing
        // one request scope must not run concurrently. With a single source this is identical to
        // parallel; when the source count makes sum-latency matter, swap this loop for a per-scope
        // parallel executor (IServiceScopeFactory / IDbContextFactory) — a change contained here.
        var groups = new List<SearchGroup>();
        foreach (var source in sources)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(SourceTimeout);

            try
            {
                var result = await source.SearchAsync(query, limit, cts.Token);
                if (result.Total > 0)
                    groups.Add(new SearchGroup(source.Source, source.Label, result.Total, result.Hits));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller (HTTP request) was cancelled — propagate rather than swallow.
                throw;
            }
            catch (Exception ex)
            {
                // Soft-timeout or source failure: omit this source, keep the rest (partial results).
                logger.LogWarning(ex, "Search source {Source} failed or timed out; omitting from results.", source.Source);
            }
        }

        return new SearchResponse(query, groups);
    }
}
