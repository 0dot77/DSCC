#if DSCC_K4A_BODY_TRACKING
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
    private static readonly SemaphoreSlim TrackerInitializationGate = new(1, 1);

    private readonly object syncRoot = new();
    private readonly Queue<DateTimeOffset> recentFrames = new();
    private readonly StickyBodySelector bodySelector = new();
    private readonly K4aBodyTrackingOptions options;
    private DateTimeOffset lastTransientErrorAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastPreviewAt = DateTimeOffset.MinValue;
    private string lastTransientError = string.Empty;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? readTask;
    private Task? previewTask;
    private Task? startTask;
    private Task? startReadyTask;
    private SensorDevice? device;
    private Tracker? tracker;
    private bool isStarting;
    private bool isRunning;

    public K4aBodyTrackingSkeletonSource(K4aBodyTrackingOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
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

            if (isStarting)
            {
                return startReadyTask ?? startTask ?? Task.CompletedTask;
            }

            var ready = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            isStarting = true;
            startReadyTask = ready.Task;
            startTask = Task.Run(() => StartCore(cancellationToken, ready), CancellationToken.None);
            return ready.Task;
        }
    }

    private void StartCore(CancellationToken cancellationToken, TaskCompletionSource<object?> ready)
    {
        SensorDevice? pendingDevice = null;
        Tracker? pendingTracker = null;
        CancellationTokenSource? pendingCancellationTokenSource = null;
        var useStartupPreview = UsesStartupPreview(options.ProcessingMode);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runtime = K4aBodyTrackingRuntimeProbe.Probe();
            if (!runtime.IsAvailable)
            {
                throw new InvalidOperationException(runtime.Status);
            }

            ReportStatus("K4A body tracking runtime ready; loading Orbbec K4A wrapper");
            K4aWrapperNativeLoader.Configure(runtime);

            var depthMode = ToDepthMode(options.DepthMode);
            var colorResolution = ColorResolution.Off;
            var deviceConfiguration = new DeviceConfiguration
            {
                CameraFPS = ToFps(options.Fps),
                ColorResolution = colorResolution,
                DepthMode = depthMode,
                SynchronizedImagesOnly = false
            };

            ReportStatus($"Opening K4A-compatible Orbbec device {FormatSerialForStatus(options.CameraSerial)}");
            pendingDevice = OpenDevice(options.CameraSerial);

            ReportStatus($"Starting depth camera ({options.DepthMode}, {options.Fps}fps)");
            pendingDevice.StartCameras(deviceConfiguration);

            var startedDevice = pendingDevice;
            if (useStartupPreview)
            {
                pendingCancellationTokenSource = new CancellationTokenSource();
                lock (syncRoot)
                {
                    var previewCancellationToken = pendingCancellationTokenSource.Token;
                    device = startedDevice;
                    cancellationTokenSource = pendingCancellationTokenSource;
                    previewTask = Task.Run(() => PreviewLoopAsync(previewCancellationToken), CancellationToken.None);
                    isRunning = true;

                    pendingDevice = null;
                    pendingCancellationTokenSource = null;
                }

                ReportStatus($"Depth preview loop started while {options.ProcessingMode} body tracker initializes");
                ready.TrySetResult(null);
            }

            ReportStatus("Reading depth camera calibration");
            var calibration = startedDevice.GetCalibration(depthMode, colorResolution);
            var trackerConfiguration = TrackerConfiguration.Default;
            trackerConfiguration.ProcessingMode = ToProcessingMode(options.ProcessingMode);
            trackerConfiguration.SensorOrientation = ToSensorOrientation(options.SensorOrientation);
            trackerConfiguration.GpuDeviceId = options.GpuDeviceId;
            if (!string.IsNullOrWhiteSpace(options.ModelPath))
            {
                trackerConfiguration.ModelPath = options.ModelPath;
            }

            ReportStatus($"Waiting for exclusive {options.ProcessingMode} body tracker initialization slot");
            TrackerInitializationGate.Wait(cancellationToken);
            try
            {
                lock (syncRoot)
                {
                    if (!isStarting || (useStartupPreview && !isRunning))
                    {
                        DisposePendingNativeObjects(pendingDevice, null, pendingCancellationTokenSource);
                        pendingDevice = null;
                        pendingCancellationTokenSource = null;
                        isStarting = false;
                        startTask = null;
                        startReadyTask = null;
                        return;
                    }
                }

                ReportStatus($"Creating {options.ProcessingMode} body tracker on GPU {options.GpuDeviceId}; first tracker initialization can take a while");
                pendingTracker = Tracker.Create(calibration, trackerConfiguration);
            }
            finally
            {
                TrackerInitializationGate.Release();
            }

            if (useStartupPreview)
            {
                Task? previewTaskToWait;
                CancellationTokenSource? previewCancellationTokenSourceToDispose;
                lock (syncRoot)
                {
                    if (!isStarting || !isRunning || cancellationTokenSource is null || cancellationTokenSource.IsCancellationRequested)
                    {
                        DisposePendingNativeObjects(null, pendingTracker, null);
                        pendingTracker = null;
                        isStarting = false;
                        startTask = null;
                        startReadyTask = null;
                        return;
                    }

                    previewTaskToWait = previewTask;
                    previewCancellationTokenSourceToDispose = cancellationTokenSource;
                    cancellationTokenSource.Cancel();
                }

                try
                {
                    previewTaskToWait?.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
                {
                }
                catch (OperationCanceledException)
                {
                }

                previewCancellationTokenSourceToDispose?.Dispose();

                lock (syncRoot)
                {
                    previewTask = null;
                    cancellationTokenSource = null;
                }
            }

            lock (syncRoot)
            {
                if (!isStarting || (useStartupPreview && !isRunning))
                {
                    DisposePendingNativeObjects(pendingDevice, pendingTracker, pendingCancellationTokenSource);
                    pendingDevice = null;
                    pendingTracker = null;
                    pendingCancellationTokenSource = null;
                    isStarting = false;
                    startTask = null;
                    startReadyTask = null;
                    return;
                }

                pendingCancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource = pendingCancellationTokenSource;
                isRunning = true;

                if (!useStartupPreview)
                {
                    device = startedDevice;
                }

                pendingDevice = null;
                pendingCancellationTokenSource = null;

                if (cancellationTokenSource is null || cancellationTokenSource.IsCancellationRequested)
                {
                    DisposePendingNativeObjects(pendingDevice, pendingTracker, pendingCancellationTokenSource);
                    pendingDevice = null;
                    pendingTracker = null;
                    pendingCancellationTokenSource = null;
                    isRunning = false;
                    isStarting = false;
                    startTask = null;
                    startReadyTask = null;
                    return;
                }

                var readCancellationToken = cancellationTokenSource.Token;
                tracker = pendingTracker;
                readTask = Task.Run(() => ReadLoopAsync(readCancellationToken), CancellationToken.None);
                isStarting = false;
                startTask = null;
                startReadyTask = null;

                pendingTracker = null;
            }

            ReportStatus("K4A body tracker read loop started");
            ready.TrySetResult(null);
        }
        catch (Exception exception)
        {
            DisposePendingNativeObjects(pendingDevice, pendingTracker, pendingCancellationTokenSource);

            Task? previewTaskToWait;
            lock (syncRoot)
            {
                isRunning = false;
                isStarting = false;
                cancellationTokenSource?.Cancel();
                TryShutdownTracker();
                previewTaskToWait = previewTask;
                startTask = null;
                startReadyTask = null;
                recentFrames.Clear();
            }

            WaitForBackgroundTaskToStop(previewTaskToWait);

            lock (syncRoot)
            {
                DisposeNativeObjects();
                bodySelector.Reset();
            }

            if (!ready.TrySetException(exception))
            {
                var message = $"K4A body tracker startup failed after depth preview started: {exception.Message}";
                ReportStatus(message);
                SourceError?.Invoke(this, message);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? readTaskToWait;
        Task? previewTaskToWait;
        lock (syncRoot)
        {
            if (!isRunning && !isStarting)
            {
                return;
            }

            isRunning = false;
            isStarting = false;
            cancellationTokenSource?.Cancel();
            TryShutdownTracker();
            readTaskToWait = readTask;
            previewTaskToWait = previewTask;
        }

        var tasksToWait = new[] { readTaskToWait, previewTaskToWait }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (tasksToWait.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasksToWait).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
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

    private async Task PreviewLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SensorDevice? currentDevice;
                lock (syncRoot)
                {
                    if (!isRunning || tracker is not null)
                    {
                        return;
                    }

                    currentDevice = device;
                }

                if (currentDevice is null)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var capture = currentDevice.GetCapture(options.CaptureTimeout);
                if (capture is null)
                {
                    await Task.Yield();
                    continue;
                }

                RaiseDepthOnlyFrame(capture, OrbbecSkeletonTrackingStatus.InitializingTrackerPreviewOnly(options.ProcessingMode));
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

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var currentDevice = device ?? throw new InvalidOperationException("K4A device is not open.");
                var currentTracker = tracker ?? throw new InvalidOperationException("K4A body tracker is not open.");

                Capture? capture = null;
                try
                {
                    capture = currentDevice.GetCapture(options.CaptureTimeout);
                    if (capture is null)
                    {
                        await Task.Yield();
                        continue;
                    }

                    var depth = capture.Depth;
                    var depthWidth = depth?.WidthPixels ?? 0;
                    var depthHeight = depth?.HeightPixels ?? 0;
                    if (!TryEnqueueCapture(currentTracker, capture))
                    {
                        RaiseDepthOnlyFrame(depthWidth, depthHeight, OrbbecSkeletonTrackingStatus.TrackerQueueBusyDroppingFrame);
                        continue;
                    }

                    capture.Dispose();
                    capture = null;

                    using var bodyFrame = currentTracker.PopResult(options.ResultTimeout, throwOnTimeout: false);
                    if (bodyFrame is null)
                    {
                        RaiseDepthOnlyFrame(depthWidth, depthHeight, OrbbecSkeletonTrackingStatus.WaitingForSkeletonResult);
                        continue;
                    }

                    RaiseFrame(bodyFrame, depthWidth, depthHeight);
                }
                finally
                {
                    capture?.Dispose();
                }
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
            ReportTransientError(OrbbecSkeletonTrackingStatus.TrackerQueueBusyDroppingFrame);
            return false;
        }
    }

    private void RaiseDepthOnlyFrame(Capture capture, string trackingStatus)
    {
        var now = DateTimeOffset.UtcNow;
        var depth = capture.Depth;
        var depthWidth = depth?.WidthPixels ?? 0;
        var depthHeight = depth?.HeightPixels ?? 0;
        var preview = CreatePreview(capture, now);
        RaiseDepthOnlyFrame(now, depthWidth, depthHeight, trackingStatus, preview);
    }

    private void RaiseDepthOnlyFrame(int depthWidth, int depthHeight, string trackingStatus)
    {
        RaiseDepthOnlyFrame(DateTimeOffset.UtcNow, depthWidth, depthHeight, trackingStatus, preview: null);
    }

    private void RaiseDepthOnlyFrame(
        DateTimeOffset now,
        int depthWidth,
        int depthHeight,
        string trackingStatus,
        DepthPreviewFrame? preview)
    {
        var estimatedFps = TrackFps(now);
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

    private void RaiseFrame(BodyTrackingFrame bodyFrame, int depthWidth, int depthHeight)
    {
        var now = DateTimeOffset.UtcNow;
        var estimatedFps = TrackFps(now);
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
            DepthPreview = null
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
            Joints = Array.Empty<JointFrameDto>(),
            BodyCount = 0,
            SelectedBodyId = -1
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
        var selectedBodyId = bodyFrame.GetBodyId((uint)bodyIndex);
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
            Joints = joints,
            BodyCount = (int)bodyFrame.NumberOfBodies,
            SelectedBodyId = selectedBodyId
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
            : TrackerProcessingMode.Cuda;
    }

    private static bool UsesStartupPreview(string processingMode)
    {
        return processingMode.Equals("Cuda", StringComparison.OrdinalIgnoreCase) ||
               processingMode.Equals("TensorRT", StringComparison.OrdinalIgnoreCase) ||
               processingMode.Equals("Gpu", StringComparison.OrdinalIgnoreCase);
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
            return OrbbecSkeletonTrackingStatus.TrackerQueueBusyDroppingFrame;
        }

        if (message.Contains("K4A_WAIT_RESULT_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return OrbbecSkeletonTrackingStatus.WaitingForSkeletonResult;
        }

        if (message.Contains("capture", StringComparison.OrdinalIgnoreCase))
        {
            return "waiting for camera capture";
        }

        return string.IsNullOrWhiteSpace(message) ? "body tracking timeout" : message;
    }

    private void ReportStatus(string message)
    {
        SourceStatus?.Invoke(this, message);
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

    private static void WaitForBackgroundTaskToStop(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        catch (OperationCanceledException)
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
        previewTask = null;
    }

    private static void DisposePendingNativeObjects(
        SensorDevice? pendingDevice,
        Tracker? pendingTracker,
        CancellationTokenSource? pendingCancellationTokenSource)
    {
        try
        {
            pendingTracker?.Shutdown();
        }
        catch
        {
        }

        pendingTracker?.Dispose();

        if (pendingDevice is not null)
        {
            try
            {
                pendingDevice.StopCameras();
            }
            catch
            {
            }

            pendingDevice.Dispose();
        }

        pendingCancellationTokenSource?.Dispose();
    }

    private static string FormatSerialForStatus(string serial)
    {
        return string.IsNullOrWhiteSpace(serial) ? "(first available)" : serial;
    }

}
#endif
