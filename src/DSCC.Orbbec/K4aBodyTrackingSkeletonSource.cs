#if DSCC_K4A_BODY_TRACKING
using System.Reflection;
using System.Runtime.InteropServices;
using DSCC.Protocol;
using Microsoft.Azure.Kinect.BodyTracking;
using Microsoft.Azure.Kinect.Sensor;
using BodyTrackingFrame = Microsoft.Azure.Kinect.BodyTracking.Frame;
using SensorDevice = Microsoft.Azure.Kinect.Sensor.Device;

namespace DSCC.Orbbec;

public sealed class K4aBodyTrackingSkeletonSource : IOrbbecSkeletonFrameSource
{
    private static readonly JointId[] TrackedJointIds = Enum.GetValues<JointId>()
        .Where(jointId => jointId != JointId.Count)
        .ToArray();

    private readonly object syncRoot = new();
    private readonly Queue<DateTimeOffset> recentFrames = new();
    private readonly StickyBodySelector bodySelector = new();
    private readonly K4aBodyTrackingOptions options;
    private DateTimeOffset lastTransientErrorAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastPreviewAt = DateTimeOffset.MinValue;
    private string lastTransientError = string.Empty;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? readTask;
    private SensorDevice? device;
    private Tracker? tracker;
    private bool isRunning;

    public K4aBodyTrackingSkeletonSource(K4aBodyTrackingOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public event EventHandler<OrbbecSkeletonFrameArrivedEventArgs>? FrameArrived;

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

            var runtime = K4aBodyTrackingRuntimeProbe.Probe();
            if (!runtime.IsAvailable)
            {
                throw new InvalidOperationException(runtime.Status);
            }

            K4aBodyTrackingNativeLoader.Configure(runtime);

            try
            {
                var depthMode = ToDepthMode(options.DepthMode);
                var colorResolution = ColorResolution.Off;
                device = OpenDevice(options.CameraSerial);
                var deviceConfiguration = new DeviceConfiguration
                {
                    CameraFPS = ToFps(options.Fps),
                    ColorResolution = colorResolution,
                    DepthMode = depthMode,
                    SynchronizedImagesOnly = false
                };

                device.StartCameras(deviceConfiguration);
                var calibration = device.GetCalibration(depthMode, colorResolution);
                var trackerConfiguration = TrackerConfiguration.Default;
                trackerConfiguration.ProcessingMode = ToProcessingMode(options.ProcessingMode);
                trackerConfiguration.SensorOrientation = ToSensorOrientation(options.SensorOrientation);
                trackerConfiguration.GpuDeviceId = options.GpuDeviceId;
                if (!string.IsNullOrWhiteSpace(options.ModelPath))
                {
                    trackerConfiguration.ModelPath = options.ModelPath;
                }

                tracker = Tracker.Create(calibration, trackerConfiguration);

                cancellationTokenSource = new CancellationTokenSource();
                readTask = Task.Run(() => ReadLoopAsync(cancellationTokenSource.Token), CancellationToken.None);
                isRunning = true;
                return Task.CompletedTask;
            }
            catch
            {
                DisposeNativeObjects();
                recentFrames.Clear();
                throw;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? taskToWait;
        lock (syncRoot)
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            cancellationTokenSource?.Cancel();
            TryShutdownTracker();
            taskToWait = readTask;
        }

        if (taskToWait is not null)
        {
            try
            {
                await taskToWait.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                SourceError?.Invoke(this, "K4A body tracking stop timed out");
            }
        }

        lock (syncRoot)
        {
            DisposeNativeObjects();
            recentFrames.Clear();
            bodySelector.Reset();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var currentDevice = device ?? throw new InvalidOperationException("K4A device is not open.");
                var currentTracker = tracker ?? throw new InvalidOperationException("K4A body tracker is not open.");

                using var capture = currentDevice.GetCapture(options.CaptureTimeout);
                if (capture is null)
                {
                    await Task.Yield();
                    continue;
                }

                if (!TryEnqueueCapture(currentTracker, capture))
                {
                    RaiseDepthOnlyFrame(capture, "tracker queue busy; dropping camera frame");
                    continue;
                }

                using var bodyFrame = currentTracker.PopResult(options.ResultTimeout, throwOnTimeout: false);
                if (bodyFrame is null)
                {
                    RaiseDepthOnlyFrame(capture, "waiting for skeleton result");
                    continue;
                }

                RaiseFrame(bodyFrame, capture);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (IsTimeoutException(exception))
            {
                ReportTransientError(FriendlyTimeoutMessage(exception.Message));
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                SourceError?.Invoke(this, exception.Message);
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool TryEnqueueCapture(Tracker currentTracker, Capture capture)
    {
        try
        {
            currentTracker.EnqueueCapture(capture, options.EnqueueTimeout);
            return true;
        }
        catch (Exception exception) when (IsTimeoutException(exception))
        {
            ReportTransientError("tracker queue busy; dropping camera frame");
            return false;
        }
    }

    private void RaiseDepthOnlyFrame(Capture capture, string trackingStatus)
    {
        var now = DateTimeOffset.UtcNow;
        var estimatedFps = TrackFps(now);
        var depth = capture.Depth;
        var depthWidth = depth?.WidthPixels ?? 0;
        var depthHeight = depth?.HeightPixels ?? 0;
        var preview = CreatePreview(capture, now);

        FrameArrived?.Invoke(this, new OrbbecSkeletonFrameArrivedEventArgs
        {
            Frame = CreateEmptyFrame(now),
            BodyCount = 0,
            DepthWidth = depthWidth,
            DepthHeight = depthHeight,
            EstimatedFps = estimatedFps,
            TrackingStatus = trackingStatus,
            PreviewMode = options.PreviewMode,
            DepthPreview = preview
        });
    }

    private void RaiseFrame(BodyTrackingFrame bodyFrame, Capture capture)
    {
        var now = DateTimeOffset.UtcNow;
        var estimatedFps = TrackFps(now);
        var depth = capture.Depth;
        var depthWidth = depth?.WidthPixels ?? 0;
        var depthHeight = depth?.HeightPixels ?? 0;
        var preview = CreatePreview(capture, now);
        var frame = bodyFrame.NumberOfBodies == 0
            ? CreateEmptyFrame(now)
            : CreateSkeletonFrame(bodyFrame, now);

        FrameArrived?.Invoke(this, new OrbbecSkeletonFrameArrivedEventArgs
        {
            Frame = frame,
            BodyCount = (int)bodyFrame.NumberOfBodies,
            DepthWidth = depthWidth,
            DepthHeight = depthHeight,
            EstimatedFps = estimatedFps,
            TrackingStatus = bodyFrame.NumberOfBodies == 0 ? "no body" : null,
            PreviewMode = options.PreviewMode,
            DepthPreview = preview
        });
    }

    private DepthPreviewFrame? CreatePreview(Capture capture, DateTimeOffset now)
    {
        if (options.PreviewInterval > TimeSpan.Zero && now - lastPreviewAt < options.PreviewInterval)
        {
            return null;
        }

        if (options.PreviewInterval > TimeSpan.Zero)
        {
            lastPreviewAt = now;
        }

        return options.PreviewMode switch
        {
            OrbbecPreviewMode.Infrared => CreateInfraredPreview(capture.IR),
            OrbbecPreviewMode.Color => null,
            _ => CreateDepthPreview(capture.Depth)
        };
    }

    private static DepthPreviewFrame? CreateDepthPreview(Image? depth)
    {
        return depth is null
            ? null
            : DepthPreviewFactory.FromDepth16(depth.GetPixels<ushort>().Span, depth.WidthPixels, depth.HeightPixels);
    }

    private static DepthPreviewFrame? CreateInfraredPreview(Image? infrared)
    {
        return infrared is null
            ? null
            : DepthPreviewFactory.FromLuma16(infrared.GetPixels<ushort>().Span, infrared.WidthPixels, infrared.HeightPixels);
    }

    private StationSkeletonFrame CreateEmptyFrame(DateTimeOffset timestamp)
    {
        return new StationSkeletonFrame
        {
            StationId = options.StationId,
            CameraSerial = options.CameraSerial,
            DeviceType = options.DeviceType,
            TimestampUsec = timestamp.ToUnixTimeMilliseconds() * 1_000,
            HasPlayer = false,
            State = StationStateDto.Empty,
            Confidence = 0.0f,
            PelvisLocal = Vector3Dto.Zero,
            BodyRotation = QuaternionDto.Identity,
            Joints = Array.Empty<JointFrameDto>()
        };
    }

    private StationSkeletonFrame CreateSkeletonFrame(BodyTrackingFrame bodyFrame, DateTimeOffset timestamp)
    {
        var bodyIndex = SelectBodyIndex(bodyFrame);
        if (bodyIndex < 0)
        {
            return CreateEmptyFrame(timestamp);
        }

        var skeleton = bodyFrame.GetBodySkeleton((uint)bodyIndex);
        var joints = TrackedJointIds
            .Select(jointId => ToJointFrameDto(jointId, skeleton.GetJoint(jointId)))
            .ToArray();
        var pelvis = skeleton.GetJoint(JointId.Pelvis);

        return new StationSkeletonFrame
        {
            StationId = options.StationId,
            CameraSerial = options.CameraSerial,
            DeviceType = options.DeviceType,
            TimestampUsec = timestamp.ToUnixTimeMilliseconds() * 1_000,
            HasPlayer = true,
            State = StationStateDto.Entering,
            Confidence = joints.Length == 0 ? 0.0f : joints.Average(joint => joint.Confidence),
            PelvisLocal = ToVector3Dto(pelvis.Position),
            BodyRotation = ToQuaternionDto(pelvis.Quaternion),
            Joints = joints
        };
    }

    private int SelectBodyIndex(BodyTrackingFrame bodyFrame)
    {
        var bodyCount = (int)bodyFrame.NumberOfBodies;
        if (bodyCount <= 0)
        {
            return -1;
        }

        var candidates = new BodyCandidate[bodyCount];
        for (var index = 0; index < bodyCount; index++)
        {
            var skeleton = bodyFrame.GetBodySkeleton((uint)index);
            var pelvis = skeleton.GetJoint(JointId.Pelvis).Position;
            candidates[index] = new BodyCandidate(
                bodyFrame.GetBodyId((uint)index),
                AverageConfidence(skeleton),
                pelvis.X / 1_000.0,
                pelvis.Y / 1_000.0,
                pelvis.Z / 1_000.0);
        }

        return bodySelector.Select(candidates, options.BodySelectionRoi);
    }

    private static double AverageConfidence(Skeleton skeleton)
    {
        var total = 0.0;
        foreach (var jointId in TrackedJointIds)
        {
            total += ConfidenceToFloat(skeleton.GetJoint(jointId).ConfidenceLevel);
        }

        return total / TrackedJointIds.Length;
    }

    private static JointFrameDto ToJointFrameDto(JointId jointId, Joint joint)
    {
        return new JointFrameDto
        {
            Name = jointId.ToString(),
            PositionLocal = ToVector3Dto(joint.Position),
            RotationLocal = ToQuaternionDto(joint.Quaternion),
            Confidence = ConfidenceToFloat(joint.ConfidenceLevel)
        };
    }

    private static Vector3Dto ToVector3Dto(System.Numerics.Vector3 millimeters)
    {
        return new Vector3Dto(
            millimeters.X / 1_000.0f,
            millimeters.Y / 1_000.0f,
            millimeters.Z / 1_000.0f);
    }

    private static QuaternionDto ToQuaternionDto(System.Numerics.Quaternion quaternion)
    {
        return new QuaternionDto(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }

    private static float ConfidenceToFloat(JointConfidenceLevel confidenceLevel)
    {
        return confidenceLevel switch
        {
            JointConfidenceLevel.None => 0.0f,
            JointConfidenceLevel.Low => 0.33f,
            JointConfidenceLevel.Medium => 0.66f,
            JointConfidenceLevel.High => 1.0f,
            _ => 0.0f
        };
    }

    private static SensorDevice OpenDevice(string serial)
    {
        var deviceCount = SensorDevice.GetInstalledCount();
        if (deviceCount <= 0)
        {
            throw new InvalidOperationException("No K4A-compatible Orbbec device was detected.");
        }

        // Devices already opened by another station throw on Open; they must be
        // skipped so every station can claim its own camera in any start order.
        var unavailableCount = 0;
        Exception? lastOpenError = null;
        for (var index = 0; index < deviceCount; index++)
        {
            SensorDevice? candidate = null;
            try
            {
                candidate = SensorDevice.Open(index);
            }
            catch (Exception exception)
            {
                unavailableCount++;
                lastOpenError = exception;
                continue;
            }

            if (string.IsNullOrWhiteSpace(serial))
            {
                return candidate;
            }

            string candidateSerial;
            try
            {
                candidateSerial = candidate.SerialNum;
            }
            catch
            {
                candidate.Dispose();
                unavailableCount++;
                continue;
            }

            if (string.Equals(candidateSerial, serial, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            candidate.Dispose();
        }

        var suffix = unavailableCount > 0
            ? $" ({unavailableCount} of {deviceCount} device(s) were busy or unavailable)"
            : string.Empty;
        throw string.IsNullOrWhiteSpace(serial)
            ? new InvalidOperationException($"No K4A-compatible Orbbec device could be opened{suffix}.", lastOpenError)
            : new InvalidOperationException($"K4A-compatible Orbbec device '{serial}' was not detected{suffix}.", lastOpenError);
    }

    private static FPS ToFps(int fps)
    {
        return fps <= 5
            ? FPS.FPS5
            : fps <= 15
                ? FPS.FPS15
                : FPS.FPS30;
    }

    private static DepthMode ToDepthMode(string depthMode)
    {
        var normalized = (depthMode ?? string.Empty)
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized switch
        {
            "NFOV_2X2BINNED" or "NFOV_BINNED" or "NFOVBINNED" => DepthMode.NFOV_2x2Binned,
            "WFOV_2X2BINNED" or "WFOV_BINNED" or "WFOVBINNED" => DepthMode.WFOV_2x2Binned,
            "WFOV_UNBINNED" or "WFOVUNBINNED" => DepthMode.WFOV_Unbinned,
            "NFOV_UNBINNED" or "NFOVUNBINNED" => DepthMode.NFOV_Unbinned,
            _ => DepthMode.WFOV_2x2Binned
        };
    }

    private static TrackerProcessingMode ToProcessingMode(string processingMode)
    {
        return Enum.TryParse<TrackerProcessingMode>(processingMode, ignoreCase: true, out var parsed)
            ? parsed
            : TrackerProcessingMode.Cpu;
    }

    private static SensorOrientation ToSensorOrientation(string sensorOrientation)
    {
        return Enum.TryParse<SensorOrientation>(sensorOrientation, ignoreCase: true, out var parsed)
            ? parsed
            : SensorOrientation.Default;
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

    private void ReportTransientError(string message)
    {
        var now = DateTimeOffset.UtcNow;
        lock (syncRoot)
        {
            if (string.Equals(lastTransientError, message, StringComparison.OrdinalIgnoreCase) &&
                now - lastTransientErrorAt < TimeSpan.FromSeconds(5))
            {
                return;
            }

            lastTransientError = message;
            lastTransientErrorAt = now;
        }

        SourceError?.Invoke(this, message);
    }

    private static bool IsTimeoutException(Exception exception)
    {
        return exception is TimeoutException ||
            exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("K4A_WAIT_RESULT_FAILED", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyTimeoutMessage(string message)
    {
        if (message.Contains("capture to be enqueued", StringComparison.OrdinalIgnoreCase))
        {
            return "tracker queue busy; dropping camera frame";
        }

        if (message.Contains("K4A_WAIT_RESULT_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return "waiting for skeleton result";
        }

        if (message.Contains("capture", StringComparison.OrdinalIgnoreCase))
        {
            return "waiting for camera capture";
        }

        return string.IsNullOrWhiteSpace(message) ? "body tracking timeout" : message;
    }

    private void TryShutdownTracker()
    {
        try
        {
            tracker?.Shutdown();
        }
        catch
        {
        }
    }

    private void DisposeNativeObjects()
    {
        tracker?.Dispose();
        tracker = null;

        if (device is not null)
        {
            try
            {
                device.StopCameras();
            }
            catch
            {
            }

            device.Dispose();
            device = null;
        }

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        readTask = null;
    }

    private static class K4aBodyTrackingNativeLoader
    {
        private static int configured;
        private static string? wrapperDirectory;
        private static IntPtr k4aHandle;
        private static IntPtr k4aRecordHandle;

        public static void Configure(K4aBodyTrackingRuntimeInfo runtime)
        {
            if (Interlocked.Exchange(ref configured, 1) == 1)
            {
                return;
            }

            wrapperDirectory = runtime.OrbbecK4aWrapperDirectory;
            if (!string.IsNullOrWhiteSpace(wrapperDirectory))
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                if (!path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(wrapperDirectory, StringComparer.OrdinalIgnoreCase))
                {
                    Environment.SetEnvironmentVariable("PATH", wrapperDirectory + Path.PathSeparator + path);
                }

                k4aHandle = Preload(Path.Combine(wrapperDirectory, "k4a.dll"));
                k4aRecordHandle = Preload(Path.Combine(wrapperDirectory, "k4arecord.dll"));
            }

            TrySetResolver(typeof(SensorDevice).Assembly, ResolveSensorNativeLibrary);
        }

        private static void TrySetResolver(Assembly assembly, DllImportResolver resolver)
        {
            try
            {
                NativeLibrary.SetDllImportResolver(assembly, resolver);
            }
            catch (InvalidOperationException)
            {
                // A resolver can be registered only once per assembly.
            }
        }

        private static IntPtr ResolveSensorNativeLibrary(
            string libraryName,
            Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            if (string.IsNullOrWhiteSpace(wrapperDirectory))
            {
                return IntPtr.Zero;
            }

            var normalized = Path.GetFileNameWithoutExtension(libraryName);
            var fileName = normalized switch
            {
                "k4a" => "k4a.dll",
                "k4arecord" => "k4arecord.dll",
                _ => null
            };
            if (fileName is null)
            {
                return IntPtr.Zero;
            }

            var path = Path.Combine(wrapperDirectory, fileName);
            return File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)
                ? handle
                : IntPtr.Zero;
        }

        private static IntPtr Preload(string path)
        {
            return File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)
                ? handle
                : IntPtr.Zero;
        }
    }
}
#endif
