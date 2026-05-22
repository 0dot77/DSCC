using Orbbec;
using System.Runtime.InteropServices;

namespace DSCC.Orbbec;

public sealed class OrbbecSdkV2FrameSource : IAsyncDisposable
{
    private readonly object syncRoot = new();
    private readonly Queue<DateTimeOffset> recentFrames = new();
    private readonly OrbbecDeviceConfiguration configuration;
    private DateTimeOffset lastPreviewAt = DateTimeOffset.MinValue;
    private Context? context;
    private Device? device;
    private Pipeline? pipeline;
    private Config? config;
    private FramesetCallback? callback;
    private bool isStreaming;

    public OrbbecSdkV2FrameSource(OrbbecDeviceInfo deviceInfo, OrbbecDeviceConfiguration? configuration = null)
    {
        DeviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        this.configuration = configuration ?? OrbbecDeviceConfiguration.ForDevice(deviceInfo);
    }

    public event EventHandler<OrbbecFrameArrivedEventArgs>? FrameArrived;

    public event EventHandler<string>? StreamError;

    public OrbbecDeviceInfo DeviceInfo { get; }

    public bool IsStreaming
    {
        get
        {
            lock (syncRoot)
            {
                return isStreaming;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (isStreaming)
            {
                return Task.CompletedTask;
            }

            context = new Context();
            using var deviceList = context.QueryDeviceList();
            device = string.IsNullOrWhiteSpace(DeviceInfo.Serial)
                ? deviceList.GetDevice(0)
                : deviceList.GetDeviceBySN(DeviceInfo.Serial);

            pipeline = new Pipeline(device);
            config = new Config();

            if (configuration.EnableDepthStream)
            {
                config.EnableVideoStream(
                    SensorType.OB_SENSOR_DEPTH,
                    width: 0,
                    height: 0,
                    fps: Math.Max(1, configuration.Fps),
                    Format.OB_FORMAT_Y16);
            }

            if (configuration.EnableInfraredStream)
            {
                config.EnableVideoStream(
                    SensorType.OB_SENSOR_IR,
                    width: 0,
                    height: 0,
                    fps: Math.Max(1, configuration.Fps),
                    Format.OB_FORMAT_Y16);
            }

            if (configuration.EnableColorStream)
            {
                config.EnableVideoStream(
                    SensorType.OB_SENSOR_COLOR,
                    width: 0,
                    height: 0,
                    fps: Math.Max(1, configuration.Fps),
                    Format.OB_FORMAT_RGB);
            }

            if (configuration.EnableFrameSync)
            {
                TryEnableFrameSync(pipeline);
            }

            callback = OnFrameset;
            pipeline.Start(config, callback);
            isStreaming = true;
            return Task.CompletedTask;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (!isStreaming)
            {
                return Task.CompletedTask;
            }

            try
            {
                pipeline?.Stop();
            }
            finally
            {
                isStreaming = false;
                callback = null;
                DisposeNativeObjects();
                recentFrames.Clear();
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void OnFrameset(Frameset frameset)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var estimatedFps = TrackFps(now);
            using var depthFrame = frameset.GetDepthFrame();
            using var infraredFrame = frameset.GetIRFrame();
            using var colorFrame = frameset.GetColorFrame();

            var timestampUsec = ReadTimestampUsec(depthFrame, infraredFrame, colorFrame, now);
            var frameIndex = ReadFrameIndex(depthFrame, infraredFrame, colorFrame);
            var preview = CreatePreview(depthFrame, infraredFrame, colorFrame, configuration.PreviewMode, now);

            FrameArrived?.Invoke(this, new OrbbecFrameArrivedEventArgs
            {
                Serial = DeviceInfo.Serial,
                FrameIndex = frameIndex,
                TimestampUsec = timestampUsec,
                FrameCount = (int)frameset.GetFrameCount(),
                HasDepth = depthFrame is not null,
                DepthWidth = depthFrame is null ? 0 : (int)depthFrame.GetWidth(),
                DepthHeight = depthFrame is null ? 0 : (int)depthFrame.GetHeight(),
                HasColor = colorFrame is not null,
                ColorWidth = colorFrame is null ? 0 : (int)colorFrame.GetWidth(),
                ColorHeight = colorFrame is null ? 0 : (int)colorFrame.GetHeight(),
                EstimatedFps = estimatedFps,
                PreviewMode = configuration.PreviewMode,
                DepthPreview = preview
            });
        }
        catch (Exception exception)
        {
            StreamError?.Invoke(this, exception.Message);
        }
        finally
        {
            frameset.Dispose();
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

    private DepthPreviewFrame? CreatePreview(
        DepthFrame? depthFrame,
        IRFrame? infraredFrame,
        ColorFrame? colorFrame,
        OrbbecPreviewMode previewMode,
        DateTimeOffset now)
    {
        var interval = configuration.PreviewInterval ?? TimeSpan.Zero;
        if (interval > TimeSpan.Zero && now - lastPreviewAt < interval)
        {
            return null;
        }

        if (interval > TimeSpan.Zero)
        {
            lastPreviewAt = now;
        }

        return previewMode switch
        {
            OrbbecPreviewMode.Infrared => CreateInfraredPreview(infraredFrame),
            OrbbecPreviewMode.Color => CreateColorPreview(colorFrame),
            _ => CreateDepthPreview(depthFrame)
        };
    }

    private static DepthPreviewFrame? CreateDepthPreview(DepthFrame? depthFrame)
    {
        if (depthFrame is null)
        {
            return null;
        }

        var width = (int)depthFrame.GetWidth();
        var height = (int)depthFrame.GetHeight();
        var byteCount = checked(width * height * sizeof(ushort));
        var bytes = new byte[byteCount];
        depthFrame.CopyData(ref bytes);
        var depth = MemoryMarshal.Cast<byte, ushort>(bytes);
        return DepthPreviewFactory.FromDepth16(depth, width, height);
    }

    private static DepthPreviewFrame? CreateInfraredPreview(IRFrame? infraredFrame)
    {
        if (infraredFrame is null)
        {
            return null;
        }

        var width = (int)infraredFrame.GetWidth();
        var height = (int)infraredFrame.GetHeight();
        var bytes = new byte[checked((int)infraredFrame.GetDataSize())];
        infraredFrame.CopyData(ref bytes);
        if (IsSixteenBitLuma(infraredFrame.GetFormat()))
        {
            return DepthPreviewFactory.FromLuma16(MemoryMarshal.Cast<byte, ushort>(bytes), width, height);
        }

        return DepthPreviewFactory.FromLuma8(bytes, width, height);
    }

    private static DepthPreviewFrame? CreateColorPreview(ColorFrame? colorFrame)
    {
        if (colorFrame is null)
        {
            return null;
        }

        var format = colorFrame.GetFormat();
        var colorFormat = format switch
        {
            Format.OB_FORMAT_RGB => ColorPreviewFormat.Rgb24,
            Format.OB_FORMAT_BGR => ColorPreviewFormat.Bgr24,
            Format.OB_FORMAT_RGBA => ColorPreviewFormat.Rgba32,
            Format.OB_FORMAT_BGRA => ColorPreviewFormat.Bgra32,
            _ => (ColorPreviewFormat?)null
        };
        if (colorFormat is null)
        {
            return null;
        }

        var width = (int)colorFrame.GetWidth();
        var height = (int)colorFrame.GetHeight();
        var bytes = new byte[checked((int)colorFrame.GetDataSize())];
        colorFrame.CopyData(ref bytes);
        return DepthPreviewFactory.FromColor(bytes, width, height, colorFormat.Value);
    }

    private static bool IsSixteenBitLuma(Format format)
    {
        return format is Format.OB_FORMAT_Y16 or
            Format.OB_FORMAT_Z16 or
            Format.OB_FORMAT_Y10 or
            Format.OB_FORMAT_Y11 or
            Format.OB_FORMAT_Y12 or
            Format.OB_FORMAT_Y14;
    }

    private static long ReadTimestampUsec(Frame? primary, Frame? secondary, Frame? tertiary, DateTimeOffset fallback)
    {
        var timestamp = primary?.GetTimeStampUs() ?? secondary?.GetTimeStampUs() ?? tertiary?.GetTimeStampUs();
        return timestamp is > 0
            ? (long)timestamp.Value
            : fallback.ToUnixTimeMilliseconds() * 1_000;
    }

    private static long ReadFrameIndex(Frame? primary, Frame? secondary, Frame? tertiary)
    {
        var index = primary?.GetIndex() ?? secondary?.GetIndex() ?? tertiary?.GetIndex();
        return index is null ? 0 : (long)index.Value;
    }

    private static void TryEnableFrameSync(Pipeline pipeline)
    {
        try
        {
            pipeline.EnableFrameSync();
        }
        catch
        {
            // Some devices/profiles do not support explicit frame sync.
        }
    }

    private void DisposeNativeObjects()
    {
        config?.Dispose();
        pipeline?.Dispose();
        device?.Dispose();
        context?.Dispose();
        config = null;
        pipeline = null;
        device = null;
        context = null;
    }
}
