using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DSCC.Protocol;
using MessagePack;

namespace DSCC.Orbbec;

public sealed class K4aBodyTrackingSidecarSource : IOrbbecSkeletonFrameSource
{
    private readonly object syncRoot = new();
    private readonly object logLock = new();
    private readonly Queue<DateTimeOffset> recentFrames = new();
    private readonly K4aBodyTrackingOptions options;
    private readonly string executablePath;
    private readonly string logDirectory;
    private UdpClient? receiver;
    private Process? process;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? receiveTask;
    private Task? monitorTask;
    private Task? stdoutTask;
    private Task? stderrTask;
    private bool emittedActiveStatus;
    private bool isRunning;

    public K4aBodyTrackingSidecarSource(
        K4aBodyTrackingOptions options,
        string executablePath,
        string logDirectory)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? throw new ArgumentException("Sidecar executable path is required.", nameof(executablePath))
            : executablePath;
        this.logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "Log", "trackers")
            : logDirectory;
    }

    public event EventHandler<OrbbecSkeletonFrameArrivedEventArgs>? FrameArrived;

    public event EventHandler<string>? SourceStatus;

    public event EventHandler<string>? SourceError;

    public bool IsRunning
    {
        get
        {
            lock (syncRoot)
            {
                return isRunning;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            if (isRunning)
            {
                return Task.CompletedTask;
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("K4ABT tracker sidecar executable was not found.", executablePath);
            }

            Directory.CreateDirectory(logDirectory);
            cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var localPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in BuildArguments(localPort))
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("K4ABT tracker sidecar failed to start.");
            isRunning = true;
            emittedActiveStatus = false;

            var token = cancellationTokenSource.Token;
            receiveTask = Task.Run(() => ReceiveLoopAsync(token), CancellationToken.None);
            monitorTask = Task.Run(() => MonitorProcessAsync(token), CancellationToken.None);
            stdoutTask = Task.Run(() => PumpProcessOutputAsync(process.StandardOutput, "stdout", token), CancellationToken.None);
            stderrTask = Task.Run(() => PumpProcessOutputAsync(process.StandardError, "stderr", token), CancellationToken.None);
        }

        SourceStatus?.Invoke(this, $"K4ABT sidecar started for station {options.StationId}; waiting for skeleton packets");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? processToStop;
        UdpClient? receiverToDispose;
        CancellationTokenSource? ctsToDispose;
        Task[] tasksToWait;

        lock (syncRoot)
        {
            if (!isRunning && process is null && receiver is null)
            {
                return;
            }

            isRunning = false;
            cancellationTokenSource?.Cancel();
            processToStop = process;
            receiverToDispose = receiver;
            ctsToDispose = cancellationTokenSource;
            tasksToWait = new[] { receiveTask, monitorTask, stdoutTask, stderrTask }
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();

            process = null;
            receiver = null;
            cancellationTokenSource = null;
            receiveTask = null;
            monitorTask = null;
            stdoutTask = null;
            stderrTask = null;
            recentFrames.Clear();
        }

        receiverToDispose?.Dispose();
        if (processToStop is not null)
        {
            try
            {
                if (!processToStop.HasExited)
                {
                    processToStop.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }

        if (tasksToWait.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasksToWait)
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        processToStop?.Dispose();
        ctsToDispose?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private IEnumerable<string> BuildArguments(int localPort)
    {
        yield return "--host";
        yield return "127.0.0.1";
        yield return "--port";
        yield return localPort.ToString(CultureInfo.InvariantCulture);
        yield return "--station-id";
        yield return options.StationId.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(options.CameraSerial))
        {
            yield return "--serial";
            yield return options.CameraSerial;
        }

        yield return "--processing-mode";
        yield return options.ProcessingMode;
        yield return "--gpu-device-id";
        yield return options.GpuDeviceId.ToString(CultureInfo.InvariantCulture);
        yield return "--depth-mode";
        yield return options.DepthMode;
        yield return "--fps";
        yield return options.Fps.ToString(CultureInfo.InvariantCulture);

        if (!options.UseLiteModel)
        {
            yield return "--full-model";
        }

        if (options.BodySelectionRoi is { } roi)
        {
            yield return "--roi";
            yield return FormatDouble(roi.MinX);
            yield return FormatDouble(roi.MaxX);
            yield return FormatDouble(roi.MinY);
            yield return FormatDouble(roi.MaxY);
            yield return FormatDouble(roi.MinZ);
            yield return FormatDouble(roi.MaxZ);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpClient? currentReceiver;
            lock (syncRoot)
            {
                currentReceiver = receiver;
            }

            if (currentReceiver is null)
            {
                return;
            }

            try
            {
                var result = await currentReceiver.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var frame = MessagePackSerializer.Deserialize<StationSkeletonFrame>(result.Buffer, cancellationToken: cancellationToken);
                if (frame.StationId != options.StationId)
                {
                    continue;
                }

                if (!emittedActiveStatus)
                {
                    emittedActiveStatus = true;
                    SourceStatus?.Invoke(this, "K4A body tracker read loop started (sidecar)");
                }

                var now = DateTimeOffset.UtcNow;
                var fps = TrackFps(now);
                var bodyCount = frame.BodyCount > 0
                    ? frame.BodyCount
                    : frame.HasPlayer ? 1 : 0;
                FrameArrived?.Invoke(this, new OrbbecSkeletonFrameArrivedEventArgs
                {
                    Frame = frame,
                    BodyCount = bodyCount,
                    DepthWidth = 0,
                    DepthHeight = 0,
                    EstimatedFps = fps,
                    TrackingStatus = frame.HasPlayer ? null : "no body",
                    PreviewMode = options.PreviewMode,
                    DepthPreview = null
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                SourceError?.Invoke(this, $"K4ABT sidecar receive failed: {exception.Message}");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task MonitorProcessAsync(CancellationToken cancellationToken)
    {
        Process? currentProcess;
        lock (syncRoot)
        {
            currentProcess = process;
        }

        if (currentProcess is null)
        {
            return;
        }

        try
        {
            await currentProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            lock (syncRoot)
            {
                isRunning = false;
            }

            SourceError?.Invoke(this, $"K4ABT sidecar exited with code {currentProcess.ExitCode}");
        }
    }

    private async Task PumpProcessOutputAsync(StreamReader reader, string streamName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            AppendSidecarLog(streamName, line);
            if (line.Contains("[tracker] error:", StringComparison.OrdinalIgnoreCase))
            {
                SourceError?.Invoke(this, line);
            }
        }
    }

    private double TrackFps(DateTimeOffset now)
    {
        lock (syncRoot)
        {
            recentFrames.Enqueue(now);
            while (recentFrames.Count > 0 && now - recentFrames.Peek() > TimeSpan.FromSeconds(2))
            {
                recentFrames.Dequeue();
            }

            if (recentFrames.Count < 2)
            {
                return 0.0;
            }

            var duration = (now - recentFrames.Peek()).TotalSeconds;
            return duration <= 0.0 ? 0.0 : (recentFrames.Count - 1) / duration;
        }
    }

    private void AppendSidecarLog(string streamName, string line)
    {
        try
        {
            var path = Path.Combine(logDirectory, $"station-{options.StationId}-sidecar.log");
            lock (logLock)
            {
                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{streamName}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
