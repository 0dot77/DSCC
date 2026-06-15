using System.IO;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSCC.App.Wpf.Services;
using DSCC.Core.Configuration;
using DSCC.Core.Stations;
using DSCC.Orbbec;
using DSCC.Protocol;
using DSCC.Replay;
using DSCC.Transport;

namespace DSCC.App.Wpf.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DsccConfigStore _configStore = new();
    private readonly ConcurrentDictionary<int, StationRuntime> _stationRuntimes = new();
    private readonly ConcurrentDictionary<int, StationFrameUiUpdate> _pendingStationUpdates = new();
    private int _stationUiFlushScheduled;
    private readonly Dictionary<string, OrbbecDeviceInfo> _discoveredOrbbecDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OrbbecStreamRuntime> _orbbecStreams = [];
    private readonly SemaphoreSlim _liveSenderGate = new(1, 1);
    private readonly HeadRotationStabilizer _headRotationStabilizer = new();
    private readonly IConfigFileDialogService _configFileDialogService;
    private CancellationTokenSource? _replayCancellation;
    private UdpMessagePackSender? _liveSkeletonSender;
    private DsccConfig _config = new();
    private bool _disposed;

    private string _wallId = "-";
    private string _configPath = string.Empty;
    private string _unityHost = "127.0.0.1";
    private int _skeletonPort = ProtocolConstants.DefaultSkeletonPort;
    private int _eventPort = ProtocolConstants.DefaultEventPort;
    private int _statusPort = ProtocolConstants.DefaultStatusPort;
    private bool _mirrorSkeletonForUnity = true;
    private bool _stabilizeHeadRotation = true;
    private double _headRotationSmoothingHalfLifeSeconds = 0.08;
    private double _headRotationMaxDegreesPerSecond = 240.0;
    private double _headRotationMinConfidence = 0.45;
    private double _headRotationDeadZoneDegrees = 0.75;
    private bool _isReplayRunning;
    private bool _isOrbbecLiveRunning;
    private bool _isLoadingConfig;
    private bool _hasUnsavedChanges;
    private int _sendFps;
    private long _packetsSent;
    private long _packetErrors;
    private string _lastSentTimestamp = "-";
    private string _unityStatus = "No heartbeat";
    private string _liveInputStatus = "Orbbec idle";
    private OrbbecPreviewMode _previewMode = OrbbecPreviewMode.Depth;
    private WorkspaceSection _selectedSection = WorkspaceSection.Workbench;

    public MainWindowViewModel()
        : this(new ConfigFileDialogService())
    {
    }

    internal MainWindowViewModel(IConfigFileDialogService configFileDialogService)
    {
        _configFileDialogService = configFileDialogService ?? throw new ArgumentNullException(nameof(configFileDialogService));
        RefreshConfigCommand = new RelayCommand(LoadConfig);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        OpenConfigCommand = new RelayCommand(OpenConfig);
        SaveConfigAsCommand = new RelayCommand(SaveConfigAs);
        RevertConfigCommand = new RelayCommand(LoadConfig);
        ExitCommand = new RelayCommand(ExitApplication);
        NavigateCommand = new RelayCommand<object?>(Navigate);
        RefreshDevicesCommand = new AsyncRelayCommand(RefreshOrbbecDevicesAsync);
        StartOrbbecLiveCommand = new AsyncRelayCommand(StartOrbbecLiveAsync, () => !IsOrbbecLiveRunning);
        StopOrbbecLiveCommand = new AsyncRelayCommand(StopOrbbecLiveAsync, () => IsOrbbecLiveRunning);
        AutoAssignDevicesCommand = new RelayCommand(AutoAssignDiscoveredDevices);
        StartReplayCommand = new AsyncRelayCommand(StartReplayAsync, () => !IsReplayRunning);
        StopReplayCommand = new RelayCommand(StopReplay, () => IsReplayRunning);
        SendTestFrameCommand = new AsyncRelayCommand(SendTestFrameAsync);
        SendEventCommand = new AsyncRelayCommand<string>(SendEventAsync);
        ApplyStationEditorCommand = new RelayCommand<int>(ApplyStationEditor);
        CaptureFootMarkerCommand = new RelayCommand<int>(CaptureFootMarker);
        GenerateRoiCommand = new RelayCommand<int>(GenerateRoi);
        ForceEnterCommand = new RelayCommand<int>(stationId => ForceStationState(stationId, StationState.Active));
        ForceExitCommand = new RelayCommand<int>(stationId => ForceStationState(stationId, StationState.Exited));
        ClearStationCommand = new RelayCommand<int>(stationId => ForceStationState(stationId, StationState.Empty));

        ConfigPath = ResolveDefaultConfigPath();
        LoadConfig();

        if (ShouldAutoStartLive())
        {
            AddLog("info", "Auto-start requested (--autostart / DSCC_AUTOSTART=1); starting Orbbec live input");
            _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (!_disposed && !IsOrbbecLiveRunning)
                {
                    await StartOrbbecLiveAsync();
                }
            });
        }
    }

    private static bool ShouldAutoStartLive()
    {
        if (Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(Environment.GetEnvironmentVariable("DSCC_AUTOSTART"), "1", StringComparison.Ordinal);
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];

    public ObservableCollection<StationRowViewModel> Stations { get; } = [];

    public ObservableCollection<string> AvailableDeviceSerials { get; } = [];

    /// <summary>Station ids a device can be pinned to; 0 = unassigned.</summary>
    public ObservableCollection<int> AssignableStationIds { get; } = [];

    public ObservableCollection<LogEntryViewModel> Logs { get; } = [];

    public IReadOnlyList<OrbbecPreviewMode> PreviewModes { get; } = Enum.GetValues<OrbbecPreviewMode>();

    public IRelayCommand RefreshConfigCommand { get; }

    public IRelayCommand SaveConfigCommand { get; }

    public IRelayCommand OpenConfigCommand { get; }

    public IRelayCommand SaveConfigAsCommand { get; }

    public IRelayCommand RevertConfigCommand { get; }

    public IRelayCommand ExitCommand { get; }

    public IRelayCommand<object?> NavigateCommand { get; }

    public IAsyncRelayCommand RefreshDevicesCommand { get; }

    public IAsyncRelayCommand StartOrbbecLiveCommand { get; }

    public IAsyncRelayCommand StopOrbbecLiveCommand { get; }

    public IRelayCommand AutoAssignDevicesCommand { get; }

    public IAsyncRelayCommand StartReplayCommand { get; }

    public IRelayCommand StopReplayCommand { get; }

    public IAsyncRelayCommand SendTestFrameCommand { get; }

    public IAsyncRelayCommand<string> SendEventCommand { get; }

    public IRelayCommand<int> ApplyStationEditorCommand { get; }

    public IRelayCommand<int> CaptureFootMarkerCommand { get; }

    public IRelayCommand<int> GenerateRoiCommand { get; }

    public IRelayCommand<int> ForceEnterCommand { get; }

    public IRelayCommand<int> ForceExitCommand { get; }

    public IRelayCommand<int> ClearStationCommand { get; }

    public WorkspaceSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(IsWorkbenchSelected));
                OnPropertyChanged(nameof(IsDevicesSelected));
                OnPropertyChanged(nameof(IsCalibrationSelected));
                OnPropertyChanged(nameof(IsUnityLinkSelected));
                OnPropertyChanged(nameof(IsDiagnosticsSelected));
                OnPropertyChanged(nameof(IsReplaySelected));
            }
        }
    }

    public bool IsWorkbenchSelected => SelectedSection == WorkspaceSection.Workbench;

    public bool IsDevicesSelected => SelectedSection == WorkspaceSection.Devices;

    public bool IsCalibrationSelected => SelectedSection == WorkspaceSection.Calibration;

    public bool IsUnityLinkSelected => SelectedSection == WorkspaceSection.UnityLink;

    public bool IsDiagnosticsSelected => SelectedSection == WorkspaceSection.Diagnostics;

    public bool IsReplaySelected => SelectedSection == WorkspaceSection.Replay;

    public string WallId
    {
        get => _wallId;
        private set => SetProperty(ref _wallId, value);
    }

    public string ConfigPath
    {
        get => _configPath;
        set => SetProperty(ref _configPath, value);
    }

    public string UnityHost
    {
        get => _unityHost;
        set
        {
            if (SetProperty(ref _unityHost, value))
            {
                MarkConfigDirty();
            }
        }
    }

    public OrbbecPreviewMode PreviewMode
    {
        get => _previewMode;
        set
        {
            if (SetProperty(ref _previewMode, value))
            {
                AddLog("info", $"Live camera preview mode changed to {value}");
                if (IsOrbbecLiveRunning)
                {
                    _ = RestartOrbbecLiveForPreviewModeAsync();
                }
            }
        }
    }

    public int SkeletonPort
    {
        get => _skeletonPort;
        set
        {
            if (SetProperty(ref _skeletonPort, value))
            {
                MarkConfigDirty();
            }
        }
    }

    public int EventPort
    {
        get => _eventPort;
        set
        {
            if (SetProperty(ref _eventPort, value))
            {
                MarkConfigDirty();
            }
        }
    }

    public int StatusPort
    {
        get => _statusPort;
        set
        {
            if (SetProperty(ref _statusPort, value))
            {
                MarkConfigDirty();
            }
        }
    }

    public bool MirrorSkeletonForUnity
    {
        get => _mirrorSkeletonForUnity;
        set
        {
            if (SetProperty(ref _mirrorSkeletonForUnity, value))
            {
                MarkConfigDirty();
            }
        }
    }

    public bool StabilizeHeadRotation
    {
        get => _stabilizeHeadRotation;
        set
        {
            if (SetProperty(ref _stabilizeHeadRotation, value))
            {
                _headRotationStabilizer.Reset();
                MarkConfigDirty();
            }
        }
    }

    public double HeadRotationSmoothingHalfLifeSeconds
    {
        get => _headRotationSmoothingHalfLifeSeconds;
        set
        {
            if (SetProperty(ref _headRotationSmoothingHalfLifeSeconds, Math.Max(0.0, value)))
            {
                _headRotationStabilizer.Reset();
                MarkConfigDirty();
            }
        }
    }

    public double HeadRotationMaxDegreesPerSecond
    {
        get => _headRotationMaxDegreesPerSecond;
        set
        {
            if (SetProperty(ref _headRotationMaxDegreesPerSecond, Math.Max(0.0, value)))
            {
                _headRotationStabilizer.Reset();
                MarkConfigDirty();
            }
        }
    }

    public double HeadRotationMinConfidence
    {
        get => _headRotationMinConfidence;
        set
        {
            if (SetProperty(ref _headRotationMinConfidence, Math.Clamp(value, 0.0, 1.0)))
            {
                _headRotationStabilizer.Reset();
                MarkConfigDirty();
            }
        }
    }

    public double HeadRotationDeadZoneDegrees
    {
        get => _headRotationDeadZoneDegrees;
        set
        {
            if (SetProperty(ref _headRotationDeadZoneDegrees, Math.Max(0.0, value)))
            {
                _headRotationStabilizer.Reset();
                MarkConfigDirty();
            }
        }
    }

    public bool IsReplayRunning
    {
        get => _isReplayRunning;
        private set
        {
            if (SetProperty(ref _isReplayRunning, value))
            {
                StartReplayCommand.NotifyCanExecuteChanged();
                StopReplayCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsOrbbecLiveRunning
    {
        get => _isOrbbecLiveRunning;
        private set
        {
            if (SetProperty(ref _isOrbbecLiveRunning, value))
            {
                StartOrbbecLiveCommand.NotifyCanExecuteChanged();
                StopOrbbecLiveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int SendFps
    {
        get => _sendFps;
        private set => SetProperty(ref _sendFps, value);
    }

    public long PacketsSent
    {
        get => _packetsSent;
        private set => SetProperty(ref _packetsSent, value);
    }

    public long PacketErrors
    {
        get => _packetErrors;
        private set => SetProperty(ref _packetErrors, value);
    }

    public string LastSentTimestamp
    {
        get => _lastSentTimestamp;
        private set => SetProperty(ref _lastSentTimestamp, value);
    }

    public string UnityStatus
    {
        get => _unityStatus;
        private set => SetProperty(ref _unityStatus, value);
    }

    public string LiveInputStatus
    {
        get => _liveInputStatus;
        private set => SetProperty(ref _liveInputStatus, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public void LoadConfig()
    {
        try
        {
            _isLoadingConfig = true;
            StopLiveStreamsForConfigReload();
            _config = _configStore.Load(ConfigPath);
            WallId = _config.WallId;
            UnityHost = _config.Unity.Host;
            SkeletonPort = _config.Unity.SkeletonPort;
            EventPort = _config.Unity.EventPort;
            StatusPort = _config.Unity.StatusPort;
            MirrorSkeletonForUnity = _config.Unity.MirrorSkeletonX;
            StabilizeHeadRotation = _config.Unity.StabilizeHeadRotation;
            HeadRotationSmoothingHalfLifeSeconds = _config.Unity.HeadRotationSmoothingHalfLifeSeconds;
            HeadRotationMaxDegreesPerSecond = _config.Unity.HeadRotationMaxDegreesPerSecond;
            HeadRotationMinConfidence = _config.Unity.HeadRotationMinConfidence;
            HeadRotationDeadZoneDegrees = _config.Unity.HeadRotationDeadZoneDegrees;
            _headRotationStabilizer.Reset();

            foreach (var station in Stations)
            {
                station.PropertyChanged -= StationRow_PropertyChanged;
            }

            Devices.Clear();
            Stations.Clear();
            _stationRuntimes.Clear();

            foreach (var stationConfig in _config.Stations.OrderBy(station => station.StationId))
            {
                var station = stationConfig.ToStation();
                var stateMachine = new StationStateMachine(station);
                var stationRow = new StationRowViewModel(
                    station.StationId,
                    station.DisplayName,
                    string.IsNullOrWhiteSpace(station.AssignedCameraSerial) ? "MOCK-REPLAY-001" : station.AssignedCameraSerial,
                    station.DeviceType,
                    station.Device.DepthMode);
                stationRow.LoadEditorValues(station);
                stationRow.PropertyChanged += StationRow_PropertyChanged;

                Stations.Add(stationRow);
                _stationRuntimes[station.StationId] = new StationRuntime(station, stateMachine, stationRow);
                var deviceRow = new DeviceRowViewModel(
                    string.IsNullOrWhiteSpace(station.DeviceType) ? "MockReplay" : station.DeviceType,
                    stationRow.AssignedCameraSerial,
                    station.StationId,
                    station.Device.Connection,
                    station.Device.Fps,
                    station.Device.DepthMode,
                    station.Device.SyncRole)
                {
                    Status = "connected"
                };
                deviceRow.PropertyChanged += DeviceRow_PropertyChanged;
                Devices.Add(deviceRow);
            }

            AssignableStationIds.Clear();
            AssignableStationIds.Add(0);
            foreach (var stationId in _stationRuntimes.Keys.OrderBy(id => id))
            {
                AssignableStationIds.Add(stationId);
            }

            UpdateAvailableDeviceSerials();

            SendFps = Devices.FirstOrDefault()?.ConfiguredFps ?? 0;
            HasUnsavedChanges = false;
            AddLog("info", $"Loaded {WallId} from {ConfigPath}");
            _ = RefreshOrbbecDevicesAsync();
        }
        catch (Exception exception)
        {
            AddLog("error", $"Config load failed: {exception.Message}");
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    private void SaveConfig()
    {
        try
        {
            ApplyAllEditorValues();
            _config.Unity.Host = UnityHost;
            _config.Unity.SkeletonPort = SkeletonPort;
            _config.Unity.EventPort = EventPort;
            _config.Unity.StatusPort = StatusPort;
            _config.Unity.MirrorSkeletonX = MirrorSkeletonForUnity;
            _config.Unity.StabilizeHeadRotation = StabilizeHeadRotation;
            _config.Unity.HeadRotationSmoothingHalfLifeSeconds = HeadRotationSmoothingHalfLifeSeconds;
            _config.Unity.HeadRotationMaxDegreesPerSecond = HeadRotationMaxDegreesPerSecond;
            _config.Unity.HeadRotationMinConfidence = HeadRotationMinConfidence;
            _config.Unity.HeadRotationDeadZoneDegrees = HeadRotationDeadZoneDegrees;
            _config.Stations = _stationRuntimes.Values
                .OrderBy(runtime => runtime.Station.StationId)
                .Select(runtime => StationConfig.FromStation(runtime.Station))
                .ToList();
            _configStore.Save(ConfigPath, _config);

            foreach (var station in Stations)
            {
                station.MarkClean();
            }

            HasUnsavedChanges = false;
            AddLog("info", $"Saved config to {ConfigPath}");
        }
        catch (Exception exception)
        {
            AddLog("error", $"Config save failed: {exception.Message}");
        }
    }

    private void OpenConfig()
    {
        if (!ConfirmDiscardUnsavedChanges("Open another config file?"))
        {
            return;
        }

        if (_configFileDialogService.TryPickOpenConfig(ConfigPath, out var selectedPath))
        {
            ConfigPath = selectedPath;
            LoadConfig();
        }
    }

    private void SaveConfigAs()
    {
        if (_configFileDialogService.TryPickSaveConfig(ConfigPath, out var selectedPath))
        {
            ConfigPath = selectedPath;
            SaveConfig();
        }
    }

    private void ExitApplication()
    {
        if (ConfirmDiscardUnsavedChanges("Exit DSCC?"))
        {
            Application.Current?.Shutdown();
        }
    }

    private bool ConfirmDiscardUnsavedChanges(string caption)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var result = MessageBox.Show(
            "There are unsaved config changes. Continue without saving?",
            caption,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private void StationRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StationRowViewModel.IsDirty)
            && sender is StationRowViewModel { IsDirty: true })
        {
            HasUnsavedChanges = true;
        }
    }

    private void MarkConfigDirty()
    {
        if (!_isLoadingConfig)
        {
            HasUnsavedChanges = true;
        }
    }

    private void ApplyAllEditorValues()
    {
        foreach (var station in Stations)
        {
            ApplyStationEditor(station.StationId, logChange: false);
        }
    }

    private void ApplyStationEditor(int stationId)
    {
        ApplyStationEditor(stationId, logChange: true);
    }

    private void ApplyStationEditor(int stationId, bool logChange)
    {
        if (!_stationRuntimes.TryGetValue(stationId, out var runtime))
        {
            return;
        }

        lock (runtime.EvaluationLock)
        {
            runtime.Row.ApplyEditorValues(runtime.Station);
            runtime.Station.Thresholds.Validate();
            runtime.StateMachine.Reset(DateTimeOffset.UtcNow);
        }

        ResolveDuplicateSerialAssignments(stationId);
        SyncDeviceAssignments();

        if (logChange)
        {
            HasUnsavedChanges = true;
            AddLog("info", $"Applied editor values to station {stationId}");
        }
    }

    private void CaptureFootMarker(int stationId)
    {
        var row = Stations.FirstOrDefault(station => station.StationId == stationId);
        if (row is null)
        {
            return;
        }

        if (row.LastFrameTime == "-")
        {
            AddLog("warn", $"Station {stationId} has no live frame to capture foot marker from");
            return;
        }

        row.FootMarkerX = row.FootX;
        row.FootMarkerY = 0.0;
        row.FootMarkerZ = row.FootZ;
        ApplyStationEditor(stationId);
        AddLog("info", $"Captured station {stationId} foot marker at X {row.FootMarkerX:0.000}, Z {row.FootMarkerZ:0.000}");
    }

    private void GenerateRoi(int stationId)
    {
        var row = Stations.FirstOrDefault(station => station.StationId == stationId);
        if (row is null)
        {
            return;
        }

        var roi = DSCC.Core.Stations.TrackingRoi.AroundFootMarker(new Vector3Meters(row.FootMarkerX, row.FootMarkerY, row.FootMarkerZ));
        row.RoiMinX = roi.MinX;
        row.RoiMaxX = roi.MaxX;
        row.RoiMinY = roi.MinY;
        row.RoiMaxY = roi.MaxY;
        row.RoiMinZ = roi.MinZ;
        row.RoiMaxZ = roi.MaxZ;
        ApplyStationEditor(stationId);
        AddLog("info", $"Generated station {stationId} ROI around foot marker");
    }

    private async Task RefreshOrbbecDevicesAsync()
    {
        try
        {
            var discovery = new OrbbecDeviceDiscovery(options: new OrbbecDeviceDiscoveryOptions
            {
                BackendMode = OrbbecBackendMode.NativeSdkV2,
                AllowPlaceholderFallback = false
            });

            var runtime = discovery.ProbeRuntime();
            if (!runtime.IsAvailable)
            {
                AddLog("warn", "Orbbec SDK runtime is not available in the app output folder");
                return;
            }

            AddLog("info", $"Orbbec SDK loaded: native {runtime.DetectedNativeSdkVersion ?? "unknown"}, C# wrapper {runtime.DetectedManagedWrapperVersion ?? "unknown"}");

            var discoveredDevices = await discovery.DiscoverAsync();
            foreach (var device in discoveredDevices)
            {
                _discoveredOrbbecDevices[device.Serial] = device;
            }

            UpdateAvailableDeviceSerials();

            foreach (var device in discoveredDevices)
            {
                UpsertOrbbecDevice(device);
            }

            if (discoveredDevices.Count == 0)
            {
                AddLog("warn", "Orbbec SDK loaded but no Femto devices were discovered");
            }
            else
            {
                AddLog("info", $"Discovered {discoveredDevices.Count} Orbbec device(s)");
            }
        }
        catch (Exception exception)
        {
            AddLog("error", $"Orbbec discovery failed: {exception.Message}");
        }
    }

    private void UpsertOrbbecDevice(OrbbecDeviceInfo deviceInfo)
    {
        var existing = Devices.FirstOrDefault(device =>
            string.Equals(device.Serial, deviceInfo.Serial, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Status = "connected";
            existing.AssignedStation = FindConfiguredStationId(deviceInfo.Serial);
            return;
        }

        var configuredStation = _config.Stations.FirstOrDefault(station =>
            string.Equals(station.Device.Serial, deviceInfo.Serial, StringComparison.OrdinalIgnoreCase));

        var firmwareStatus = OrbbecFirmwarePolicy.IsRecommendedOrNewer(deviceInfo)
            ? "connected"
            : $"connected; FW {deviceInfo.FirmwareVersion ?? "unknown"} < recommended {OrbbecFirmwarePolicy.RecommendedFirmwareFor(deviceInfo.DeviceType)}";

        var discoveredRow = new DeviceRowViewModel(
            deviceInfo.DeviceType.ToString(),
            deviceInfo.Serial,
            configuredStation?.StationId ?? 0,
            deviceInfo.Connection.ToString(),
            configuredStation?.Device.Fps ?? 30,
            configuredStation?.Device.DepthMode ?? "SDK_PROFILE",
            configuredStation?.Device.SyncRole ?? "SDK")
        {
            Status = firmwareStatus
        };
        discoveredRow.PropertyChanged += DeviceRow_PropertyChanged;
        Devices.Insert(0, discoveredRow);
    }

    private bool _isSyncingDeviceAssignments;

    private void DeviceRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSyncingDeviceAssignments ||
            e.PropertyName != nameof(DeviceRowViewModel.AssignedStation) ||
            sender is not DeviceRowViewModel device)
        {
            return;
        }

        AssignDeviceToStation(device.Serial, device.AssignedStation);
    }

    /// <summary>
    /// Pins a device serial to a station (0 releases it). The serial is
    /// removed from any other station so one camera never feeds two stations.
    /// </summary>
    private void AssignDeviceToStation(string serial, int stationId)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        if (stationId > 0 && _stationRuntimes.TryGetValue(stationId, out var target))
        {
            target.Row.AssignedCameraSerial = serial;
            if (_discoveredOrbbecDevices.TryGetValue(serial, out var deviceInfo))
            {
                target.Row.DeviceType = deviceInfo.DeviceType.ToString();
            }

            ApplyStationEditor(stationId, logChange: false);
            AddLog("info", $"Pinned {serial} to station {stationId} (save config to persist)");
        }
        else
        {
            foreach (var runtime in _stationRuntimes.Values)
            {
                if (string.Equals(runtime.Station.AssignedCameraSerial, serial, StringComparison.OrdinalIgnoreCase))
                {
                    runtime.Row.AssignedCameraSerial = string.Empty;
                    ApplyStationEditor(runtime.Station.StationId, logChange: false);
                }
            }

            AddLog("info", $"Released {serial} from station assignment");
        }

        UpdateAvailableDeviceSerials();
        SyncDeviceAssignments();
    }

    /// <summary>
    /// Last writer wins: when a serial is applied to one station, any other
    /// station still holding it is released.
    /// </summary>
    private void ResolveDuplicateSerialAssignments(int ownerStationId)
    {
        if (!_stationRuntimes.TryGetValue(ownerStationId, out var owner))
        {
            return;
        }

        var serial = owner.Station.AssignedCameraSerial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        foreach (var other in _stationRuntimes.Values)
        {
            if (other.Station.StationId == ownerStationId ||
                !string.Equals(other.Station.AssignedCameraSerial, serial, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            other.Row.AssignedCameraSerial = string.Empty;
            lock (other.EvaluationLock)
            {
                other.Station.AssignedCameraSerial = string.Empty;
            }

            AddLog("info", $"Station {other.Station.StationId} released {serial}; it is now pinned to station {ownerStationId}");
        }
    }

    private void UpdateAvailableDeviceSerials()
    {
        // Discovered serials plus serials pinned in config, so offline
        // cameras stay selectable and visible.
        var serials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serial in _discoveredOrbbecDevices.Keys)
        {
            serials.Add(serial);
        }

        foreach (var runtime in _stationRuntimes.Values)
        {
            if (!string.IsNullOrWhiteSpace(runtime.Station.AssignedCameraSerial))
            {
                serials.Add(runtime.Station.AssignedCameraSerial);
            }
        }

        AvailableDeviceSerials.Clear();
        foreach (var serial in serials)
        {
            AvailableDeviceSerials.Add(serial);
        }
    }

    private int FindConfiguredStationId(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return 0;
        }

        return _stationRuntimes.Values
            .FirstOrDefault(runtime => string.Equals(runtime.Station.AssignedCameraSerial, serial, StringComparison.OrdinalIgnoreCase))
            ?.Station.StationId ?? 0;
    }

    private void SyncDeviceAssignments()
    {
        _isSyncingDeviceAssignments = true;
        try
        {
            foreach (var device in Devices)
            {
                device.AssignedStation = FindConfiguredStationId(device.Serial);
            }
        }
        finally
        {
            _isSyncingDeviceAssignments = false;
        }
    }

    private HashSet<string> CollectAssignedSerials()
    {
        return new HashSet<string>(
            _stationRuntimes.Values
                .Select(runtime => runtime.Station.AssignedCameraSerial)
                .Where(serial => !string.IsNullOrWhiteSpace(serial)),
            StringComparer.OrdinalIgnoreCase);
    }

    private void AutoAssignDiscoveredDevices()
    {
        // Devices already pinned to a station must not be handed out again.
        var assignedSerials = CollectAssignedSerials();
        var freeDevices = new Queue<OrbbecDeviceInfo>(_discoveredOrbbecDevices.Values
            .OrderBy(device => device.Serial)
            .Where(device => !string.IsNullOrWhiteSpace(device.Serial) && !assignedSerials.Contains(device.Serial)));

        foreach (var runtime in _stationRuntimes.Values.OrderBy(runtime => runtime.Station.StationId))
        {
            if (!string.IsNullOrWhiteSpace(runtime.Row.AssignedCameraSerial))
            {
                continue;
            }

            if (!freeDevices.TryDequeue(out var device))
            {
                break;
            }

            runtime.Row.AssignedCameraSerial = device.Serial;
            runtime.Row.DeviceType = device.DeviceType.ToString();
            ApplyStationEditor(runtime.Station.StationId);
        }

        SyncDeviceAssignments();
        AddLog("info", "Auto-assigned discovered Orbbec devices to empty stations");
    }

    private void Navigate(object? parameter)
    {
        if (TryParseWorkspaceSection(parameter, out var section))
        {
            SelectedSection = section;
        }
    }

    private static bool TryParseWorkspaceSection(object? value, out WorkspaceSection section)
    {
        switch (value)
        {
            case WorkspaceSection workspaceSection:
                section = workspaceSection;
                return true;
            case string text when !string.IsNullOrWhiteSpace(text):
                var normalized = text.Trim()
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace("_", string.Empty, StringComparison.Ordinal);
                return Enum.TryParse(normalized, ignoreCase: true, out section);
            default:
                section = default;
                return false;
        }
    }

    private async Task StartOrbbecLiveAsync()
    {
        if (IsOrbbecLiveRunning)
        {
            return;
        }

        if (IsReplayRunning)
        {
            StopReplay();
        }

        await RefreshOrbbecDevicesAsync().ConfigureAwait(true);

        if (_discoveredOrbbecDevices.Count == 0)
        {
            LiveInputStatus = "No Orbbec device discovered";
            AddLog("warn", "Cannot start Orbbec live input: no device discovered");
            return;
        }

        // By default only explicitly pinned serials are used; opt-in config
        // flag restores fill-empty-stations behavior for unattended setups.
        if (_config.AutoAssignDevicesOnStart)
        {
            var assignedSerials = CollectAssignedSerials();
            var hasUnassignedDevice = _discoveredOrbbecDevices.Values.Any(device =>
                !string.IsNullOrWhiteSpace(device.Serial) && !assignedSerials.Contains(device.Serial));
            var hasEmptyStation = _stationRuntimes.Values.Any(runtime =>
                string.IsNullOrWhiteSpace(runtime.Station.AssignedCameraSerial));
            if (hasUnassignedDevice && hasEmptyStation)
            {
                AutoAssignDiscoveredDevices();
            }
        }

        _headRotationStabilizer.Reset();
        var bodyTrackingRuntime = K4aBodyTrackingRuntimeProbe.Probe();
        AddLog(bodyTrackingRuntime.IsAvailable ? "info" : "warn", bodyTrackingRuntime.Status);
        var canUseBodyTracking = bodyTrackingRuntime.IsAvailable && PreviewMode != OrbbecPreviewMode.Color;
        if (bodyTrackingRuntime.IsAvailable && PreviewMode == OrbbecPreviewMode.Color)
        {
            AddLog("info", "Color preview runs as camera diagnostics; K4A body tracking remains depth-only");
        }

        if (canUseBodyTracking)
        {
            _liveSkeletonSender = new UdpMessagePackSender(UnityHost, SkeletonPort);
        }

        var bodyTracking = _config.BodyTracking ?? new BodyTrackingConfig();
        var processingModes = bodyTracking.ProcessingModes is { Count: > 0 }
            ? bodyTracking.ProcessingModes
            : ["DirectML", "Cpu"];
        var previewInterval = TimeSpan.FromMilliseconds(Math.Max(0.0, bodyTracking.PreviewIntervalMilliseconds));
        var modelPath = string.Empty;
        if (canUseBodyTracking && bodyTracking.UseLiteModel)
        {
            modelPath = ResolveLiteModelPath();
            AddLog(
                string.IsNullOrEmpty(modelPath) ? "warn" : "info",
                string.IsNullOrEmpty(modelPath)
                    ? "Lite body tracking model requested but dnn_model_2_0_lite_op11.onnx was not found; using the full model"
                    : "Body tracking uses the lite model (dnn_model_2_0_lite_op11.onnx)");
        }

        foreach (var runtime in _stationRuntimes.Values.OrderBy(runtime => runtime.Station.StationId))
        {
            var serial = runtime.Station.AssignedCameraSerial;
            if (string.IsNullOrWhiteSpace(serial) || !_discoveredOrbbecDevices.TryGetValue(serial, out var deviceInfo))
            {
                continue;
            }

            if (canUseBodyTracking &&
                await TryStartBodyTrackingChainAsync(runtime, deviceInfo, processingModes, modelPath, previewInterval).ConfigureAwait(true))
            {
                continue;
            }

            var configuration = OrbbecDeviceConfiguration.ForDevice(deviceInfo) with
            {
                StationId = runtime.Station.StationId,
                Fps = Math.Max(1, runtime.Station.Device.Fps),
                PreviewMode = PreviewMode,
                PreviewInterval = previewInterval,
                EnableColorStream = PreviewMode == OrbbecPreviewMode.Color,
                EnableInfraredStream = PreviewMode == OrbbecPreviewMode.Infrared,
                EnableDepthStream = PreviewMode == OrbbecPreviewMode.Depth,
                EnableFrameSync = true
            };
            var source = new OrbbecSdkV2FrameSource(deviceInfo, configuration);
            source.FrameArrived += (_, args) => OnOrbbecFrameArrived(runtime.Station.StationId, args);
            source.StreamError += (_, message) => OnOrbbecStreamError(runtime.Station.StationId, serial, message);

            try
            {
                await source.StartAsync().ConfigureAwait(true);
                _orbbecStreams.Add(new OrbbecStreamRuntime(runtime.Station.StationId, source, null));
                var device = Devices.FirstOrDefault(candidate => string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
                if (device is not null)
                {
                    device.Status = $"streaming {PreviewMode.ToString().ToLowerInvariant()}";
                    device.SkeletonStatus = "body tracking SDK not connected";
                }

                runtime.Row.UpdateCameraFrame(
                    $"streaming {PreviewMode.ToString().ToLowerInvariant()}",
                    "-",
                    0,
                    DateTimeOffset.Now,
                    "camera stream active; skeleton provider missing");
                AddLog("info", $"Started Orbbec {PreviewMode.ToString().ToLowerInvariant()} stream for station {runtime.Station.StationId} ({serial})");
            }
            catch (Exception exception)
            {
                await source.DisposeAsync().ConfigureAwait(true);
                AddLog("error", $"Orbbec stream start failed for station {runtime.Station.StationId}: {exception.Message}");
            }
        }

        IsOrbbecLiveRunning = _orbbecStreams.Count > 0;
        var skeletonStreamCount = _orbbecStreams.Count(stream => stream.SkeletonSource is not null);
        var depthStreamCount = _orbbecStreams.Count(stream => stream.Source is not null);
        LiveInputStatus = IsOrbbecLiveRunning
            ? skeletonStreamCount > 0
                ? $"Orbbec K4A skeleton streams: {skeletonStreamCount}; depth fallback streams: {depthStreamCount}"
                : $"Orbbec live depth streams: {depthStreamCount}; skeleton provider missing"
            : "No mapped Orbbec stream started";

        if (!IsOrbbecLiveRunning)
        {
            _liveSkeletonSender?.Dispose();
            _liveSkeletonSender = null;
        }
    }

    private async Task<bool> TryStartBodyTrackingChainAsync(
        StationRuntime runtime,
        OrbbecDeviceInfo deviceInfo,
        IReadOnlyList<string> processingModes,
        string modelPath,
        TimeSpan previewInterval)
    {
        for (var index = 0; index < processingModes.Count; index++)
        {
            var processingMode = processingModes[index];
            if (RequiresCudaRuntime(processingMode) && !CudaRuntimeProbe.IsLikelyAvailable())
            {
                AddLog("warn", $"Skipping {processingMode} body tracking for station {runtime.Station.StationId}: {CudaRuntimeProbe.Describe()}");
                continue;
            }

            var isLastMode = index == processingModes.Count - 1;
            if (await TryStartBodyTrackingSourceAsync(runtime, deviceInfo, processingMode, modelPath, previewInterval, isLastMode).ConfigureAwait(true))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresCudaRuntime(string processingMode)
    {
        return processingMode.Equals("Cuda", StringComparison.OrdinalIgnoreCase) ||
               processingMode.Equals("TensorRT", StringComparison.OrdinalIgnoreCase) ||
               processingMode.Equals("Gpu", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLiteModelPath()
    {
        var litePath = Path.Combine(AppContext.BaseDirectory, "dnn_model_2_0_lite_op11.onnx");
        return File.Exists(litePath) ? litePath : string.Empty;
    }

    private async Task<bool> TryStartBodyTrackingSourceAsync(
        StationRuntime runtime,
        OrbbecDeviceInfo deviceInfo,
        string processingMode,
        string modelPath,
        TimeSpan previewInterval,
        bool isLastMode)
    {
        var bodyTracking = _config.BodyTracking ?? new BodyTrackingConfig();
        var configuredFps = Math.Max(1, runtime.Station.Device.Fps);
        var effectiveFps = Math.Min(Math.Max(1, bodyTracking.MaxFps), configuredFps);
        var roi = runtime.Station.TrackingRoi;
        var modelTag = string.IsNullOrEmpty(modelPath) ? "full model" : "lite model";
        var source = K4aBodyTrackingSkeletonSourceFactory.Create(new K4aBodyTrackingOptions
        {
            StationId = runtime.Station.StationId,
            CameraSerial = runtime.Station.AssignedCameraSerial,
            DeviceType = deviceInfo.DeviceType.ToString(),
            Fps = effectiveFps,
            DepthMode = runtime.Station.Device.DepthMode,
            ProcessingMode = processingMode,
            ModelPath = modelPath,
            GpuDeviceId = bodyTracking.GpuDeviceId,
            BodySelectionRoi = new BodySelectionRoi(roi.MinX, roi.MaxX, roi.MinY, roi.MaxY, roi.MinZ, roi.MaxZ),
            SensorOrientation = "Default",
            PreviewMode = PreviewMode,
            PreviewInterval = previewInterval
        });
        source.FrameArrived += (_, args) => OnOrbbecSkeletonFrameArrived(runtime.Station.StationId, deviceInfo.Serial, args);
        source.SourceError += (_, message) => OnOrbbecSkeletonSourceError(runtime.Station.StationId, deviceInfo.Serial, message);

        try
        {
            await source.StartAsync().ConfigureAwait(true);
            _orbbecStreams.Add(new OrbbecStreamRuntime(runtime.Station.StationId, null, source));
            var device = Devices.FirstOrDefault(candidate => string.Equals(candidate.Serial, deviceInfo.Serial, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                device.Status = "streaming skeleton";
                device.SkeletonStatus = $"K4A body tracking active ({processingMode}, {modelTag}, {effectiveFps}fps, {runtime.Station.Device.DepthMode})";
            }

            runtime.Row.UpdateCameraFrame(
                "streaming skeleton",
                "-",
                0,
                DateTimeOffset.Now,
                $"K4A body tracking active ({processingMode}, {modelTag}, {effectiveFps}fps, {runtime.Station.Device.DepthMode})");
            AddLog("info", $"Started K4A body tracking for station {runtime.Station.StationId} ({deviceInfo.Serial}, {processingMode}, {modelTag}, {effectiveFps}fps, {runtime.Station.Device.DepthMode})");
            return true;
        }
        catch (Exception exception)
        {
            await source.DisposeAsync().ConfigureAwait(true);
            AddLog(
                isLastMode ? "error" : "warn",
                $"K4A body tracking {processingMode} start failed for station {runtime.Station.StationId}: {exception.Message}");
            return false;
        }
    }

    private async Task StopOrbbecLiveAsync()
    {
        foreach (var stream in _orbbecStreams.ToArray())
        {
            try
            {
                if (stream.Source is not null)
                {
                    await stream.Source.DisposeAsync().ConfigureAwait(true);
                }

                if (stream.SkeletonSource is not null)
                {
                    await stream.SkeletonSource.DisposeAsync().ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                AddLog("error", $"Orbbec stream stop failed for station {stream.StationId}: {exception.Message}");
            }
        }

        _orbbecStreams.Clear();
        _liveSkeletonSender?.Dispose();
        _liveSkeletonSender = null;
        IsOrbbecLiveRunning = false;
        LiveInputStatus = "Orbbec idle";

        foreach (var device in Devices)
        {
            if (device.Status.StartsWith("streaming", StringComparison.OrdinalIgnoreCase))
            {
                device.Status = "connected";
            }
        }

        AddLog("info", "Stopped Orbbec live input");
    }

    private async Task RestartOrbbecLiveForPreviewModeAsync()
    {
        try
        {
            await StopOrbbecLiveAsync().ConfigureAwait(true);
            await StartOrbbecLiveAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AddLog("error", $"Orbbec preview mode restart failed: {exception.Message}");
        }
    }

    private void OnOrbbecFrameArrived(int stationId, OrbbecFrameArrivedEventArgs args)
    {
        QueueStationUiUpdate(stationId, new StationFrameUiUpdate
        {
            Serial = args.Serial,
            CameraStatus = $"streaming {args.PreviewMode.ToString().ToLowerInvariant()}",
            Resolution = args.HasDepth ? $"{args.DepthWidth}x{args.DepthHeight}" : "no depth",
            EstimatedFps = args.EstimatedFps,
            FrameTime = DateTimeOffset.FromUnixTimeMilliseconds(args.TimestampUsec / 1_000),
            SkeletonStatus = "camera stream active; skeleton provider missing",
            Preview = args.DepthPreview,
            PreviewMode = args.PreviewMode,
            HasColor = args.HasColor,
            ColorWidth = args.ColorWidth,
            ColorHeight = args.ColorHeight,
            FramesDelta = 1
        });
    }

    private void OnOrbbecSkeletonFrameArrived(int stationId, string serial, OrbbecSkeletonFrameArrivedEventArgs args)
    {
        if (!_stationRuntimes.TryGetValue(stationId, out var runtime))
        {
            return;
        }

        // Runs on the source's read thread: domain evaluation and the Unity
        // send stay off the UI thread; only the coalesced snapshot below is
        // marshaled to the dispatcher.
        var frame = args.Frame;
        var (evaluation, footX, footZ) = EvaluateStationFrame(runtime, frame);

        if (_liveSkeletonSender is not null)
        {
            _ = SendLiveFrameAsync(frame);
        }

        var skeletonStatus = !string.IsNullOrWhiteSpace(args.TrackingStatus)
            ? args.TrackingStatus
            : args.BodyCount > 0
            ? $"tracking {args.BodyCount} body"
            : "no body";

        QueueStationUiUpdate(stationId, new StationFrameUiUpdate
        {
            Serial = serial,
            CameraStatus = "streaming skeleton",
            Resolution = args.DepthWidth > 0 && args.DepthHeight > 0
                ? $"{args.DepthWidth}x{args.DepthHeight}"
                : "depth unavailable",
            EstimatedFps = args.EstimatedFps,
            FrameTime = DateTimeOffset.FromUnixTimeMilliseconds(frame.TimestampUsec / 1_000),
            SkeletonStatus = skeletonStatus,
            Preview = args.DepthPreview,
            PreviewMode = args.PreviewMode,
            Frame = frame,
            Evaluation = evaluation,
            FootX = footX,
            FootZ = footZ,
            FramesDelta = 1
        });
    }

    private void QueueStationUiUpdate(int stationId, StationFrameUiUpdate update)
    {
        _pendingStationUpdates.AddOrUpdate(
            stationId,
            update,
            (_, previous) => update with
            {
                // A newer frame without a preview must not erase a pending one.
                Preview = update.Preview ?? previous.Preview,
                FramesDelta = previous.FramesDelta + update.FramesDelta
            });

        if (Interlocked.CompareExchange(ref _stationUiFlushScheduled, 1, 0) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Volatile.Write(ref _stationUiFlushScheduled, 0);
            return;
        }

        _ = dispatcher.BeginInvoke(FlushStationUiUpdates, DispatcherPriority.Input);
    }

    private void FlushStationUiUpdates()
    {
        Volatile.Write(ref _stationUiFlushScheduled, 0);
        foreach (var stationId in _pendingStationUpdates.Keys)
        {
            if (_pendingStationUpdates.TryRemove(stationId, out var update))
            {
                ApplyStationUiUpdate(stationId, update);
            }
        }
    }

    private void ApplyStationUiUpdate(int stationId, StationFrameUiUpdate update)
    {
        if (!_stationRuntimes.TryGetValue(stationId, out var runtime))
        {
            return;
        }

        var frameTimeText = update.FrameTime.ToLocalTime().ToString("HH:mm:ss.fff");
        if (update.CameraStatus is not null)
        {
            var device = update.Serial is null
                ? null
                : Devices.FirstOrDefault(candidate => string.Equals(candidate.Serial, update.Serial, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                device.Status = update.CameraStatus;
                device.FramesReceived += update.FramesDelta;
                device.Fps = update.EstimatedFps;
                device.DepthResolution = update.Resolution ?? "-";
                device.ColorResolution = update.HasColor ? $"{update.ColorWidth}x{update.ColorHeight}" : "-";
                device.LastFrameTime = frameTimeText;
                if (update.SkeletonStatus is not null)
                {
                    device.SkeletonStatus = update.SkeletonStatus;
                }
            }

            runtime.Row.UpdateCameraFrame(
                update.CameraStatus,
                update.Resolution ?? "-",
                update.EstimatedFps,
                update.FrameTime,
                update.SkeletonStatus ?? runtime.Row.SkeletonSourceStatus);
        }

        if (update.Preview is not null)
        {
            runtime.Row.UpdateDepthPreview(update.Preview, update.PreviewMode);
        }

        if (update.Frame is { } frame && update.Evaluation is { } evaluation)
        {
            runtime.Row.State = evaluation.State.ToString();
            runtime.Row.HasPlayer = evaluation.HasPlayer;
            runtime.Row.Confidence = frame.Confidence;
            runtime.Row.InsideRoi = evaluation.IsInsideTrackingRoi;
            runtime.Row.InsideFootMarker = evaluation.IsInsideFootMarker;
            runtime.Row.LostSeconds = (float)evaluation.TrackingLostSeconds;
            runtime.Row.LastFrameTime = frameTimeText;
            runtime.Row.PelvisX = frame.PelvisLocal.X;
            runtime.Row.PelvisY = frame.PelvisLocal.Y;
            runtime.Row.PelvisZ = frame.PelvisLocal.Z;
            runtime.Row.FootX = update.FootX;
            runtime.Row.FootZ = update.FootZ;
            var jointCount = frame.Joints.Length;
            var trackedJointCount = frame.Joints.Count(joint => joint.Confidence >= runtime.Station.Thresholds.MinSkeletonConfidence);
            var averageJointConfidence = jointCount == 0
                ? 0.0
                : frame.Joints.Average(joint => joint.Confidence);
            runtime.Row.UpdateSkeletonDiagnostics(jointCount, trackedJointCount, averageJointConfidence);
            runtime.Row.UpdateSkeletonOverlay(frame.Joints);
        }

        if (update.EstimatedFps > 0)
        {
            SendFps = Math.Max(0, (int)Math.Round(update.EstimatedFps));
        }
    }

    private void OnOrbbecSkeletonSourceError(int stationId, string serial, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => OnOrbbecSkeletonSourceError(stationId, serial, message));
            return;
        }

        var isTransient = IsTransientSkeletonSourceMessage(message);
        var device = Devices.FirstOrDefault(candidate => string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (device is not null)
        {
            device.Status = isTransient ? "streaming skeleton" : "skeleton error";
            device.SkeletonStatus = message;
            device.DroppedFrames++;
        }

        if (_stationRuntimes.TryGetValue(stationId, out var runtime))
        {
            runtime.Row.UpdateCameraFrame(
                isTransient ? "streaming skeleton" : "skeleton error",
                runtime.Row.CameraFrameResolution,
                runtime.Row.CameraFps,
                DateTimeOffset.Now,
                message);
        }

        AddLog(isTransient ? "warn" : "error", $"K4A body tracking {(isTransient ? "status" : "error")} on station {stationId}: {message}");
    }

    private static bool IsTransientSkeletonSourceMessage(string message)
    {
        return message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("K4A_WAIT_RESULT_FAILED", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("queue busy", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("waiting for camera capture", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SendLiveFrameAsync(StationSkeletonFrame frame)
    {
        var sender = _liveSkeletonSender;
        if (sender is null)
        {
            return;
        }

        var lockTaken = false;
        try
        {
            await _liveSenderGate.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
            if (!ReferenceEquals(sender, _liveSkeletonSender))
            {
                return;
            }

            var unityFrame = PrepareFrameForUnity(frame);
            await sender.SendAsync(unityFrame, CancellationToken.None).ConfigureAwait(false);
            PacketsSent++;
            LastSentTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(unityFrame.TimestampUsec / 1_000)
                .ToLocalTime()
                .ToString("HH:mm:ss.fff");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            PacketErrors++;
            AddLog("error", $"Live skeleton send failed: {exception.Message}");
        }
        finally
        {
            if (lockTaken)
            {
                _liveSenderGate.Release();
            }
        }
    }

    private void OnOrbbecStreamError(int stationId, string serial, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => OnOrbbecStreamError(stationId, serial, message));
            return;
        }

        var device = Devices.FirstOrDefault(candidate => string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (device is not null)
        {
            device.Status = "stream error";
            device.DroppedFrames++;
        }

        AddLog("error", $"Orbbec stream error on station {stationId}: {message}");
    }

    private async Task StartReplayAsync()
    {
        if (IsReplayRunning)
        {
            return;
        }

        IsReplayRunning = true;
        _replayCancellation = new CancellationTokenSource();
        _headRotationStabilizer.Reset();
        var cancellationToken = _replayCancellation.Token;

        foreach (var device in Devices)
        {
            device.Status = "streaming";
        }

        AddLog("info", "Mock replay started");

        try
        {
            await using var sender = new UdpMessagePackSender(UnityHost, SkeletonPort);
            var generator = new FakeSkeletonFrameGenerator();

            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var runtime in _stationRuntimes.Values.OrderBy(runtime => runtime.Station.StationId))
                {
                    var options = CreateReplayOptions(runtime.Station);

                    await foreach (var frame in generator.PlaySequenceAsync(options, cancellationToken).ConfigureAwait(true))
                    {
                        var evaluatedFrame = ApplyStationEvaluation(runtime, frame);
                        // Replay frames are stage-space; Unity expects the live K4A camera convention.
                        await SendFrameAsync(sender, ReplayFrameConventions.ToK4aCameraConvention(evaluatedFrame), cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("info", "Mock replay stopped");
        }
        catch (Exception exception)
        {
            PacketErrors++;
            AddLog("error", $"Replay failed: {exception.Message}");
        }
        finally
        {
            IsReplayRunning = false;
            _replayCancellation?.Dispose();
            _replayCancellation = null;

            foreach (var device in Devices)
            {
                device.Status = "connected";
            }
        }
    }

    private void StopReplay()
    {
        _replayCancellation?.Cancel();
    }

    private async Task SendTestFrameAsync()
    {
        var runtime = _stationRuntimes.Values.OrderBy(runtime => runtime.Station.StationId).FirstOrDefault();
        if (runtime is null)
        {
            AddLog("warn", "No station is configured");
            return;
        }

        var generator = new FakeSkeletonFrameGenerator();
        var frame = generator.CreateSequence(CreateReplayOptions(runtime.Station))
            .FirstOrDefault(candidate => candidate.State == StationStateDto.Active);

        if (frame is null)
        {
            AddLog("warn", "No active test frame was generated");
            return;
        }

        await using var sender = new UdpMessagePackSender(UnityHost, SkeletonPort);
        await SendFrameAsync(sender, ReplayFrameConventions.ToK4aCameraConvention(ApplyStationEvaluation(runtime, frame)), CancellationToken.None);
        AddLog("info", "Sent one active StationSkeletonFrame");
    }

    private async Task SendEventAsync(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        try
        {
            await using var sender = new UdpJsonEventSender(UnityHost, EventPort);
            await sender.SendAsync(new DsccEvent
            {
                EventType = eventName,
                StationId = Stations.FirstOrDefault()?.StationId,
                TimestampUsec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000
            });

            AddLog("info", $"Sent event {eventName}");
        }
        catch (Exception exception)
        {
            PacketErrors++;
            AddLog("error", $"Event send failed: {exception.Message}");
        }
    }

    private void ForceStationState(int stationId, StationState state)
    {
        if (!_stationRuntimes.TryGetValue(stationId, out var runtime))
        {
            return;
        }

        lock (runtime.EvaluationLock)
        {
            runtime.StateMachine.Reset(DateTimeOffset.UtcNow);
            runtime.Station.State = state;
        }
        runtime.Row.State = state.ToString();
        runtime.Row.HasPlayer = state is StationState.Entering or StationState.Active;
        runtime.Row.LastFrameTime = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
        AddLog("info", $"Station {stationId} forced to {state}");
    }

    private async ValueTask SendFrameAsync(
        IStationSkeletonSender sender,
        StationSkeletonFrame frame,
        CancellationToken cancellationToken)
    {
        try
        {
            var unityFrame = PrepareFrameForUnity(frame);
            await sender.SendAsync(unityFrame, cancellationToken);
            PacketsSent++;
            LastSentTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(unityFrame.TimestampUsec / 1_000)
                .ToLocalTime()
                .ToString("HH:mm:ss.fff");
        }
        catch
        {
            PacketErrors++;
            throw;
        }
    }

    private StationSkeletonFrame PrepareFrameForUnity(StationSkeletonFrame frame)
    {
        var unityFrame = StabilizeHeadRotation
            ? _headRotationStabilizer.Apply(frame, CreateHeadRotationStabilizerOptions())
            : frame;

        return MirrorSkeletonForUnity
            ? SkeletonFrameTransforms.MirrorPerformerFacingCamera(unityFrame)
            : unityFrame;
    }

    private HeadRotationStabilizerOptions CreateHeadRotationStabilizerOptions()
    {
        return new HeadRotationStabilizerOptions
        {
            Enabled = StabilizeHeadRotation,
            SmoothingHalfLifeSeconds = (float)HeadRotationSmoothingHalfLifeSeconds,
            MaxDegreesPerSecond = (float)HeadRotationMaxDegreesPerSecond,
            MinConfidence = (float)HeadRotationMinConfidence,
            DeadZoneDegrees = (float)HeadRotationDeadZoneDegrees
        };
    }

    private StationSkeletonFrame ApplyStationEvaluation(StationRuntime runtime, StationSkeletonFrame frame)
    {
        var (evaluation, footX, footZ) = EvaluateStationFrame(runtime, frame);
        QueueStationUiUpdate(runtime.Station.StationId, new StationFrameUiUpdate
        {
            FrameTime = DateTimeOffset.FromUnixTimeMilliseconds(frame.TimestampUsec / 1_000),
            Frame = frame,
            Evaluation = evaluation,
            FootX = footX,
            FootZ = footZ
        });

        return frame;
    }

    private (StationTrackingEvaluation Evaluation, double FootX, double FootZ) EvaluateStationFrame(
        StationRuntime runtime,
        StationSkeletonFrame frame)
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(frame.TimestampUsec / 1_000);
        var pelvis = new Vector3Meters(frame.PelvisLocal.X, frame.PelvisLocal.Y, frame.PelvisLocal.Z);
        var fallbackFoot = new Vector3Meters(frame.PelvisLocal.X, 0.0, frame.PelvisLocal.Z);
        Vector3Meters? leftFoot = TryFindJoint(frame, "FootLeft", out var footLeft)
            ? footLeft
            : TryFindJoint(frame, "AnkleLeft", out var ankleLeft)
                ? ankleLeft
                : null;
        Vector3Meters? rightFoot = TryFindJoint(frame, "FootRight", out var footRight)
            ? footRight
            : TryFindJoint(frame, "AnkleRight", out var ankleRight)
                ? ankleRight
                : null;
        var primaryFoot = AverageFeet(leftFoot, rightFoot) ?? leftFoot ?? rightFoot ?? fallbackFoot;
        var sample = frame.State is StationStateDto.Empty
            ? SkeletonTrackingSample.Lost(timestamp)
            : SkeletonTrackingSample.Detected(pelvis, primaryFoot, frame.Confidence, timestamp);

        sample.LeftFootPosition = leftFoot;
        sample.RightFootPosition = rightFoot;
        sample.CameraSerial = frame.CameraSerial;

        StationTrackingEvaluation evaluation;
        lock (runtime.EvaluationLock)
        {
            evaluation = runtime.StateMachine.Update(sample, timestamp);

            frame.State = ToDto(evaluation.State);
            frame.HasPlayer = evaluation.HasPlayer;
            frame.IsInsideTrackingRoi = evaluation.IsInsideTrackingRoi;
            frame.IsInsideFootMarker = evaluation.IsInsideFootMarker;
            frame.TrackingLostSeconds = (float)evaluation.TrackingLostSeconds;
            frame.Confidence = (float)evaluation.Confidence;
            frame.AnchorPosition = new Vector3Dto(
                (float)runtime.Station.UnityAnchor.X,
                (float)runtime.Station.UnityAnchor.Y,
                (float)runtime.Station.UnityAnchor.Z);
            frame.AnchorRotationYDegrees = (float)runtime.Station.UnityAnchor.RotationY;
        }

        return (evaluation, primaryFoot.X, primaryFoot.Z);
    }

    private static bool TryFindJoint(StationSkeletonFrame frame, string name, out Vector3Meters position)
    {
        var joint = frame.Joints.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (joint is null)
        {
            position = default;
            return false;
        }

        position = new Vector3Meters(joint.PositionLocal.X, joint.PositionLocal.Y, joint.PositionLocal.Z);
        return true;
    }

    private static Vector3Meters? AverageFeet(Vector3Meters? leftFoot, Vector3Meters? rightFoot)
    {
        if (leftFoot is null || rightFoot is null)
        {
            return null;
        }

        return new Vector3Meters(
            (leftFoot.Value.X + rightFoot.Value.X) / 2.0,
            (leftFoot.Value.Y + rightFoot.Value.Y) / 2.0,
            (leftFoot.Value.Z + rightFoot.Value.Z) / 2.0);
    }

    private FakeSkeletonFrameOptions CreateReplayOptions(Station station)
    {
        return new FakeSkeletonFrameOptions
        {
            StationId = station.StationId,
            CameraSerial = string.IsNullOrWhiteSpace(station.AssignedCameraSerial)
                ? "MOCK-REPLAY-001"
                : station.AssignedCameraSerial,
            DeviceType = string.IsNullOrWhiteSpace(station.DeviceType) ? "MockReplay" : station.DeviceType,
            Fps = Math.Max(1, station.Device.Fps),
            TrackingRoi = new DSCC.Replay.TrackingRoi
            {
                MinX = (float)station.TrackingRoi.MinX,
                MaxX = (float)station.TrackingRoi.MaxX,
                MinY = (float)station.TrackingRoi.MinY,
                MaxY = (float)station.TrackingRoi.MaxY,
                MinZ = (float)station.TrackingRoi.MinZ,
                MaxZ = (float)station.TrackingRoi.MaxZ
            },
            FootMarkerCenter = new Vector3Dto(
                (float)station.FootMarkerCenter.X,
                (float)station.FootMarkerCenter.Y,
                (float)station.FootMarkerCenter.Z),
            FootMarkerRadiusMeters = (float)station.Thresholds.FootMarkerRadiusMeters,
            InsideConfidence = (float)Math.Max(station.Thresholds.MinSkeletonConfidence + 0.25, 0.8)
        };
    }

    private void AddLog(string level, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => AddLog(level, message));
            return;
        }

        Logs.Insert(0, new LogEntryViewModel(DateTimeOffset.Now, level, message));
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        AppendLogToFile(level, message);
    }

    private static readonly object LogFileLock = new();

    private static void AppendLogToFile(string level, string message)
    {
        try
        {
            lock (LogFileLock)
            {
                Directory.CreateDirectory("Log");
                File.AppendAllText(
                    Path.Combine("Log", "dscc-app.log"),
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static StationStateDto ToDto(StationState state)
    {
        return state switch
        {
            StationState.Empty => StationStateDto.Empty,
            StationState.Entering => StationStateDto.Entering,
            StationState.Active => StationStateDto.Active,
            StationState.Lost => StationStateDto.Lost,
            StationState.Exited => StationStateDto.Exited,
            StationState.Disabled => StationStateDto.Disabled,
            StationState.Error => StationStateDto.Error,
            _ => StationStateDto.Error
        };
    }

    private static string ResolveDefaultConfigPath()
    {
        const string localRelativePath = "config/wall-a.local.json";
        const string exampleRelativePath = "config/wall-a.example.json";

        foreach (var directory in ConfigSearchRoots())
        {
            var localPath = Path.Combine(directory.FullName, localRelativePath);
            if (File.Exists(localPath))
            {
                return localPath;
            }

            var examplePath = Path.Combine(directory.FullName, exampleRelativePath);
            if (!File.Exists(examplePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.Copy(examplePath, localPath, overwrite: false);
            return localPath;
        }

        return Path.GetFullPath(localRelativePath);
    }

    private static IEnumerable<DirectoryInfo> ConfigSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (seen.Add(directory.FullName))
                {
                    yield return directory;
                }

                directory = directory.Parent;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _replayCancellation?.Cancel();
        _replayCancellation?.Dispose();
        foreach (var stream in _orbbecStreams.ToArray())
        {
            stream.Source?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            stream.SkeletonSource?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        _orbbecStreams.Clear();
        _liveSkeletonSender?.Dispose();
    }

    private void StopLiveStreamsForConfigReload()
    {
        if (IsReplayRunning)
        {
            StopReplay();
        }

        if (_orbbecStreams.Count == 0)
        {
            return;
        }

        // Old sources reference the runtimes being torn down; detach them and
        // dispose off the UI thread (their stop paths block on read loops).
        var streamsToDispose = _orbbecStreams.ToArray();
        _orbbecStreams.Clear();
        _liveSkeletonSender?.Dispose();
        _liveSkeletonSender = null;
        IsOrbbecLiveRunning = false;
        LiveInputStatus = "Orbbec idle";
        _ = Task.Run(async () =>
        {
            foreach (var stream in streamsToDispose)
            {
                try
                {
                    if (stream.Source is not null)
                    {
                        await stream.Source.DisposeAsync().ConfigureAwait(false);
                    }

                    if (stream.SkeletonSource is not null)
                    {
                        await stream.SkeletonSource.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    AddLog("error", $"Orbbec stream stop failed for station {stream.StationId}: {exception.Message}");
                }
            }
        });
        AddLog("info", "Stopped Orbbec live input for config reload");
    }

    private sealed record StationRuntime(
        Station Station,
        StationStateMachine StateMachine,
        StationRowViewModel Row)
    {
        public object EvaluationLock { get; } = new();
    }

    private sealed record OrbbecStreamRuntime(
        int StationId,
        OrbbecSdkV2FrameSource? Source,
        IOrbbecSkeletonFrameSource? SkeletonSource);

    private sealed record StationFrameUiUpdate
    {
        public string? Serial { get; init; }

        public string? CameraStatus { get; init; }

        public string? Resolution { get; init; }

        public double EstimatedFps { get; init; }

        public DateTimeOffset FrameTime { get; init; }

        public string? SkeletonStatus { get; init; }

        public DepthPreviewFrame? Preview { get; init; }

        public OrbbecPreviewMode PreviewMode { get; init; }

        public StationSkeletonFrame? Frame { get; init; }

        public StationTrackingEvaluation? Evaluation { get; init; }

        public double FootX { get; init; }

        public double FootZ { get; init; }

        public bool HasColor { get; init; }

        public int ColorWidth { get; init; }

        public int ColorHeight { get; init; }

        public int FramesDelta { get; init; }
    }
}
