using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

/// <summary>
/// StopAsync's defensive branches, reached by reflecting private host state into shapes the
/// happy path never produces: a held stop gate, a wedged run task that misses the bounded stop
/// deadline, a temp-file sweep on an alive versus an already-disposed container, and a disposed
/// shutdown CTS. None of these hosts is started on Kestrel, so no port, database or sidecar
/// state is touched beyond the shared-bin hygiene of the BinState collection.
/// </summary>
[Collection("BinState")]
public sealed class WebApiHostServiceStopPathTests : IDisposable
{
    public WebApiHostServiceStopPathTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task StopAsync_WhenStopGateIsAlreadyHeld_ReturnsWithoutStopping()
    {
        WebApiHostService host = CreateHost();
        try
        {
            WriteIsRunning(host, true);
            SemaphoreSlim stopGate = ReadStopGate(host);
            Assert.True(stopGate.Wait(0));
            try
            {
                await host.StopAsync();

                Assert.True(ReadIsRunning(host));
            }
            finally
            {
                stopGate.Release();
            }
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_WhenRunTaskMissesTheStopDeadline_ContinuesShutdownAfterLogging()
    {
        WebApiHostService host = CreateHost();
        try
        {
            Task wedgedRunTask = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            WriteField(host, "_runTask", wedgedRunTask);
            WriteIsRunning(host, true);

            await host.StopAsync();

            Assert.False(ReadIsRunning(host));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_WhenContainerIsAlive_CompletesTheTempFileSweep()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        WebApplication aliveApp = builder.Build();
        WebApiHostService host = CreateHost();
        try
        {
            WriteField(host, "_app", aliveApp);
            WriteField(host, "_runTask", Task.CompletedTask);
            WriteIsRunning(host, true);

            Exception? failure = await Record.ExceptionAsync(() => host.StopAsync());

            Assert.Null(failure);
            Assert.False(ReadIsRunning(host));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_WhenContainerWasAlreadyDisposed_SkipsTheTempFileSweepGracefully()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        WebApplication disposedApp = builder.Build();
        await disposedApp.DisposeAsync();
        WebApiHostService host = CreateHost();
        try
        {
            WriteField(host, "_app", disposedApp);
            WriteField(host, "_runTask", Task.CompletedTask);
            WriteIsRunning(host, true);

            Exception? failure = await Record.ExceptionAsync(() => host.StopAsync());

            Assert.Null(failure);
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_WhenShutdownCtsWasDisposed_SwallowsTheFailureAndLogs()
    {
        WebApiHostService host = CreateHost();
        try
        {
            CancellationTokenSource disposedCts = new CancellationTokenSource();
            disposedCts.Dispose();
            WriteField(host, "_cts", disposedCts);
            WriteIsRunning(host, true);

            Exception? failure = await Record.ExceptionAsync(() => host.StopAsync());

            Assert.Null(failure);
        }
        finally
        {
            host.Dispose();
        }
    }

    private static WebApiHostService CreateHost()
    {
        ScannerServiceConfiguration configuration = new ScannerServiceConfiguration
        {
            ApiPort = 50777,
            ApiHost = "localhost",
            ShutdownTimeoutMs = 2000
        };
        return new WebApiHostService(configuration, new LocalSettingsStore(configuration));
    }

    private static SemaphoreSlim ReadStopGate(WebApiHostService host)
    {
        FieldInfo stopGateField = typeof(WebApiHostService).GetField("_stopGate", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Private field _stopGate not found");
        return (SemaphoreSlim)stopGateField.GetValue(host)!;
    }

    private static void WriteField(WebApiHostService host, string fieldName, object? value)
    {
        FieldInfo field = typeof(WebApiHostService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Private field " + fieldName + " not found");
        field.SetValue(host, value);
    }

    private static bool ReadIsRunning(WebApiHostService host)
    {
        PropertyInfo isRunningProperty = typeof(WebApiHostService).GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Property IsRunning not found");
        return (bool)isRunningProperty.GetValue(host)!;
    }

    private static void WriteIsRunning(WebApiHostService host, bool value)
    {
        PropertyInfo isRunningProperty = typeof(WebApiHostService).GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Property IsRunning not found");
        isRunningProperty.SetValue(host, value);
    }
}
