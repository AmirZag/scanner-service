using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAPS2.Images.Gdi;
using NAPS2.Scan;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for the real ScannerInitializer lifecycle, headless: constructing the GdiImageContext and
/// ScanController touches no scanner hardware, and a failed TWAIN worker setup is swallowed by the
/// implementation (setting TwainWorkerFailed instead), so every path here completes cleanly on a
/// machine without scanners. The memoization and dispose-state pins use the internal
/// IScannerInitializerContext seam to observe the created context.
/// </summary>
public sealed class ScannerInitializerTests : IAsyncLifetime
{
    private ScannerInitializer? _initializer;
    private bool _initializerDisposedInTest;

    public Task InitializeAsync()
    {
        _initializer = new ScannerInitializer(NullLogger<ScannerInitializer>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_initializer is not null && !_initializerDisposedInTest)
        {
            await _initializer.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_FirstCall_InitializesCleanly()
    {
        ScannerInitializer initializer = _initializer!;

        await initializer.InitializeAsync();

        Assert.True(initializer.IsInitialized);
        Assert.NotNull(((IScannerInitializerContext)initializer).Context);
        Assert.NotNull(((IScannerInitializerContext)initializer).Controller);
    }

    [Fact]
    public async Task InitializeAsync_SecondCall_MemoizedAndStillInitialized()
    {
        ScannerInitializer initializer = _initializer!;
        await initializer.InitializeAsync();
        ScanningContext firstContext = ((IScannerInitializerContext)initializer).Context;

        await initializer.InitializeAsync();

        Assert.True(initializer.IsInitialized);
        ScanningContext secondContext = ((IScannerInitializerContext)initializer).Context;
        Assert.Same(firstContext, secondContext);
    }

    [Fact]
    public async Task InitializeAsync_FiveConcurrentCalls_AllCompleteWithSingleInitialization()
    {
        ScannerInitializer initializer = _initializer!;

        IEnumerable<Task> initializeCalls = Enumerable.Range(0, 5).Select(_ => initializer.InitializeAsync());
        await Task.WhenAll(initializeCalls);

        Assert.True(initializer.IsInitialized);
        Assert.NotNull(((IScannerInitializerContext)initializer).Context);
        Assert.NotNull(((IScannerInitializerContext)initializer).Controller);
    }

    // ScannerInitializer.DisposeAsync disposes the context and the initialization semaphore but
    // deliberately leaves the initialized flags untouched (no reset in the current
    // implementation). Pin that: if a future change resets state on dispose, this assertion and
    // the test below must be revisited together.
    [Fact]
    public async Task DisposeAsync_InitializedInitializer_CurrentBehavior_DoesNotResetInitializedState()
    {
        ScannerInitializer initializer = _initializer!;
        await initializer.InitializeAsync();
        Assert.True(initializer.IsInitialized);

        await initializer.DisposeAsync();
        _initializerDisposedInTest = true;

        Assert.True(initializer.IsInitialized);
    }

    // Because dispose does not reset the initialized state, a later InitializeAsync takes the
    // fast path and short-circuits instead of re-initializing. Pin the no-throw fast-path
    // behavior and the unchanged context.
    [Fact]
    public async Task InitializeAsync_AfterDispose_CurrentBehavior_ShortCircuitsWithoutReinitializing()
    {
        ScannerInitializer initializer = _initializer!;
        await initializer.InitializeAsync();
        ScanningContext contextBeforeDispose = ((IScannerInitializerContext)initializer).Context;
        await initializer.DisposeAsync();
        _initializerDisposedInTest = true;

        await initializer.InitializeAsync();

        Assert.True(initializer.IsInitialized);
        Assert.Same(contextBeforeDispose, ((IScannerInitializerContext)initializer).Context);
    }

    // The lazy-init state is private, so the faulted-slot injection below is the only seam into
    // the memoization recovery logic; the pragma follows the production pattern for justified
    // Sonar suppressions.
#pragma warning disable S3011 // Reflection should not be used to increase usability of coded tests
    [Fact]
    public async Task InitializeAsync_InFlightInitializationFaults_ClearsSlotAndThrowsWithoutMemoizing()
    {
        ScannerInitializer initializer = _initializer!;
        GetPrivateField("_initializationTask").SetValue(
            initializer,
            Task.FromException(new InvalidOperationException("cached initialization failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync());

        Assert.Null(GetPrivateField("_initializationTask").GetValue(initializer));

        // The slot was cleared, so the next call retries instead of replaying the cached failure.
        await initializer.InitializeAsync();
        Assert.True(initializer.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_InitializationInternalFails_ClearsSlotAndRetriesCleanly()
    {
        ScannerInitializer initializer = new ScannerInitializer(new ThrowingOnceLogger());
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync());
            Assert.False(initializer.IsInitialized);
            Assert.Null(GetPrivateField("_initializationTask").GetValue(initializer));

            await initializer.InitializeAsync();
            Assert.True(initializer.IsInitialized);
        }
        finally
        {
            await initializer.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_ContextDisposeFails_SwallowsErrorAndCompletesShutdown()
    {
        ScannerInitializer initializer = _initializer!;
        ScanningContext scanningContext = new ScanningContext(new GdiImageContext());
        ThrowingOnDisposeProbe probe = new ThrowingOnDisposeProbe();
        HashSet<IDisposable> disposables = GetContextDisposables(scanningContext);
        disposables.Add(probe);
        GetPrivateField("_context").SetValue(initializer, scanningContext);

        // The dispose failure must not break the shutdown sequence; the probe count proves the
        // context Dispose actually ran (and threw) while DisposeAsync still completed.
        await initializer.DisposeAsync();
        _initializerDisposedInTest = true;

        Assert.Equal(1, probe.DisposeCallCount);
    }

    private static FieldInfo GetPrivateField(string fieldName)
    {
        return typeof(ScannerInitializer).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field " + fieldName + " was not found on ScannerInitializer.");
    }

    private static HashSet<IDisposable> GetContextDisposables(ScanningContext scanningContext)
    {
        object processedImageOwner = typeof(ScanningContext)
            .GetField("_processedImageOwner", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(scanningContext)!;

        return (HashSet<IDisposable>)processedImageOwner
            .GetType()
            .GetField("_disposables", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(processedImageOwner)!;
    }
#pragma warning restore S3011 // Reflection should not be used to increase usability of coded tests

    /// <summary>
    /// Logger that throws on the first Information-level call, which is the initialization
    /// trace emitted at the start of InitializeInternal. Later calls (the retry and shutdown)
    /// pass through untouched, so the retry path can complete for real.
    /// </summary>
    private sealed class ThrowingOnceLogger : ILogger<ScannerInitializer>
    {
        private int _informationLogCount;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information && Interlocked.Increment(ref _informationLogCount) == 1)
            {
                throw new InvalidOperationException("injected scanner initialization failure");
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }
    }

    /// <summary>
    /// Registered into a real ScanningContext's disposable set so its Dispose throws; this is
    /// what turns ScanningContext.Dispose into a failure without any NAPS2 internals being
    /// replaced.
    /// </summary>
    private sealed class ThrowingOnDisposeProbe : IDisposable
    {
        public int DisposeCallCount { get; private set; }

        public void Dispose()
        {
            DisposeCallCount++;
            throw new InvalidOperationException("injected scanning context dispose failure");
        }
    }
}
