using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// Concurrency pins for StockService. These fire real parallel HTTP requests against the Testcontainers
// Postgres: a single authenticated HttpClient is safe for concurrent sends, and each request gets its own
// DI scope, AppDbContext, connection, and command transaction — so these exercise genuine cross-transaction
// races, which InMemory could never honour. The stock funnel's guarded writes are the code under test.
[Collection("Integration")]
public class StockConcurrencyTests : IntegrationTestBase
{
    public StockConcurrencyTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers (mirrors ConsumablesEndpointTests' style) -----

    private static async Task<ConsumableItem> CreateConsumableAsync(HttpClient client, string name = "Screws")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/receive", body, CT);

    private static Task<HttpResponseMessage> ConsumeAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/consume", body, CT);

    private static async Task<ConsumableDetailItem> GetConsumableAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/consumables/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableDetailItem>(CT))!;
    }

    private static async Task<IReadOnlyList<StockMovementItem>> GetMovementsAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/consumables/{id}/movements?page=1&pageSize=100", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PaginatedResult<StockMovementItem>>(CT);
        return page!.Items;
    }

    // Ledger-replay: net on-hand = Σ(inflow magnitudes) − Σ(outflow magnitudes). Transfers (both ends set)
    // net to zero and don't appear here. This is the invariant stock.md promises always holds.
    private static decimal NetFromLedger(IReadOnlyList<StockMovementItem> movements)
    {
        decimal net = 0m;
        foreach (var m in movements)
        {
            var from = m.FromAreaId is not null || m.FromContainerItemId is not null;
            var to = m.ToAreaId is not null || m.ToContainerItemId is not null;
            if (to && !from) net += m.Quantity;       // inflow
            else if (from && !to) net -= m.Quantity;   // outflow
        }
        return net;
    }

    // ----- Tests -----

    [Fact]
    public async Task ConcurrentConsumes_NeverOverdraw()
    {
        // Regression pin for the existing guarded decrement: N concurrent single-unit consumes against a
        // stock of Q must let exactly Q succeed and reject the rest — never driving the row negative.
        const int q = 5;
        const int n = 12;
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = (decimal)q });

        var responses = await Task.WhenAll(
            Enumerable.Range(0, n).Select(_ => ConsumeAsync(client, consumable.Id, new { areaId, quantity = 1m })));

        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failures = responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(q, successes);
        Assert.Equal(n - q, failures);
        Assert.All(responses, r =>
            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
                $"Unexpected status {(int)r.StatusCode}"));

        // The location is fully drained: no entry left, zero on-hand.
        var detail = await GetConsumableAsync(client, consumable.Id);
        Assert.Equal(0m, detail.Consumable.TotalOnHand);
        Assert.Empty(detail.Locations);

        // Exactly Q outflow movements were recorded — one per successful consume, no phantom rows.
        var outflows = (await GetMovementsAsync(client, consumable.Id))
            .Count(m => (m.FromAreaId is not null || m.FromContainerItemId is not null)
                && m.ToAreaId is null && m.ToContainerItemId is null);
        Assert.Equal(q, outflows);
    }

    [Fact]
    public async Task ConcurrentReceiveAndDrain_ReceiveNeverVanishes()
    {
        // Regression pin for A1 (fails before the increment re-insert fix). A consume that purges the entry
        // to zero can race an increment whose ExecuteUpdate then touches 0 rows — pre-fix the +5 is lost
        // from the entry while the inflow movement is still appended, breaking the ledger-replay invariant.
        // Reproduction is probabilistic (stress loop to hit the purge-between-read-and-update window), but
        // EVERY iteration asserts the invariant, so a single racy hit fails the pre-fix build.
        const int iterations = 25;
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");

        for (var i = 0; i < iterations; i++)
        {
            var consumable = await CreateConsumableAsync(client, $"Bolts {i}");
            await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 1m });   // seed a purgeable entry

            var receiveTask = ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
            var consumeTask = ConsumeAsync(client, consumable.Id, new { areaId, quantity = 1m });
            await Task.WhenAll(receiveTask, consumeTask);
            var receive = await receiveTask;
            var consume = await consumeTask;

            // Both orderings net to +5 (1 + 5 − 1); neither should ever fail on a valid seed.
            Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
            Assert.Equal(HttpStatusCode.OK, consume.StatusCode);

            var detail = await GetConsumableAsync(client, consumable.Id);
            var movements = await GetMovementsAsync(client, consumable.Id);
            Assert.Equal(NetFromLedger(movements), detail.Consumable.TotalOnHand); // invariant every iteration
            Assert.Equal(5m, detail.Consumable.TotalOnHand);                        // stock never vanishes
        }
    }

    [Fact]
    public async Task ConcurrentFirstReceives_NeverRaw500()
    {
        // Covers the new 23505 -> 409 mapping. Two parallel first-receives into a brand-new consumable at one
        // fresh location both take the create branch; the loser on the NULLS NOT DISTINCT unique index must
        // surface as a retryable 409, never a raw 500 (or, if it reads after the winner commits, it merges
        // and both succeed). Either interleaving leaves exactly one entry equal to the successful receives.
        const int iterations = 25;
        const decimal n = 3m;
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");

        for (var i = 0; i < iterations; i++)
        {
            var consumable = await CreateConsumableAsync(client, $"Nuts {i}");

            var responses = await Task.WhenAll(
                ReceiveAsync(client, consumable.Id, new { areaId, quantity = n }),
                ReceiveAsync(client, consumable.Id, new { areaId, quantity = n }));

            Assert.All(responses, r =>
                Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                    $"Expected 200 or 409 but got {(int)r.StatusCode}"));
            Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK); // at least one always commits

            var expectedOnHand = n * responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            var detail = await GetConsumableAsync(client, consumable.Id);
            Assert.Equal(expectedOnHand, detail.Consumable.TotalOnHand);
            var entry = Assert.Single(detail.Locations);   // never two rows at one location
            Assert.Equal(expectedOnHand, entry.Quantity);
        }
    }
}
