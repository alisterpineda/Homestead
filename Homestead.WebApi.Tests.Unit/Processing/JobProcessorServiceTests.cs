using Homestead.WebApi.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class JobProcessorServiceTests
{
    private readonly ILogger<JobProcessorService> _logger = Substitute.For<ILogger<JobProcessorService>>();

    private JobProcessorService CreateService(int maxConcurrency = 1)
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var options = Options.Create(new JobProcessingOptions { MaxConcurrency = maxConcurrency });
        var runner = new JobRunner(
            sp.GetRequiredService<IServiceScopeFactory>(), options, Substitute.For<ILogger<JobRunner>>());
        return new JobProcessorService(runner, options, _logger);
    }

    [Fact]
    public void Signal_ImplementsIJobSignal()
    {
        var service = CreateService();
        IJobSignal signal = service;

        // Should not throw
        signal.Signal();
    }

    [Fact]
    public async Task PauseAsync_ReturnsDisposableHandle()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        await using var handle = await service.PauseAsync(ct);

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task PauseAsync_SecondCallBlocks_UntilFirstReleased()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var handle1 = await service.PauseAsync(ct);

        // Second call should block since the gate is held
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.PauseAsync(cts.Token));

        // After releasing, a new pause should succeed
        await handle1.DisposeAsync();
        await using var handle2 = await service.PauseAsync(ct);
        Assert.NotNull(handle2);
    }

    [Fact]
    public async Task PauseHandle_DoubleDispose_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var handle = await service.PauseAsync(ct);
        await handle.DisposeAsync();
        await handle.DisposeAsync(); // Should not throw or double-release
    }

    [Fact]
    public async Task PauseAsync_WithConcurrency_AcquiresAllSlots()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService(maxConcurrency: 3);

        // First pause acquires all 3 slots
        var handle1 = await service.PauseAsync(ct);

        // Second pause should block since all slots are held
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.PauseAsync(cts.Token));

        // After releasing, all 3 slots are available again
        await handle1.DisposeAsync();
        await using var handle2 = await service.PauseAsync(ct);
        Assert.NotNull(handle2);
    }
}
