using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DSCC.Core.Stations;
using DSCC.Orbbec;
using DSCC.Protocol;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DSCC.App.Wpf.ViewModels;

public sealed class StationRowViewModel : ObservableObject
{
    private const double EditorCanvasWidth = 180.0;
    private const double EditorCanvasTop = 28.0;
    private const double EditorCanvasHeight = 160.0;
    private const double EditorMinX = -1.5;
    private const double EditorMaxX = 1.5;
    private const double EditorMinZ = 0.25;
    private const double EditorMaxZ = 5.5;
    private const double MarkerRadius = 5.0;
    private const double DefaultPreviewWidth = 160.0;
    private const double DefaultPreviewHeight = 144.0;
    private string _state = "Empty";
    private string _assignedCameraSerial = string.Empty;
    private string _deviceType = string.Empty;
    private string _depthMode = string.Empty;
    private bool _hasPlayer;
    private float _confidence;
    private bool _insideRoi;
    private bool _insideFootMarker;
    private float _lostSeconds;
    private string _lastFrameTime = "-";
    private string _cameraFrameStatus = "not streaming";
    private string _cameraFrameResolution = "-";
    private string _cameraFrameTime = "-";
    private string _livePreviewStatus = "waiting for depth preview";
    private ImageSource? _liveDepthPreview;
    private double _livePreviewCanvasWidth = DefaultPreviewWidth;
    private double _livePreviewCanvasHeight = DefaultPreviewHeight;
    private double _cameraFps;
    private string _skeletonSourceStatus = "waiting for skeleton source";
    private int _jointCount;
    private int _trackedJointCount;
    private double _averageJointConfidence;
    private double _jointCoverage;
    private bool _isDirty;
    private double _pelvisX;
    private double _pelvisY;
    private double _pelvisZ;
    private double _footX;
    private double _footZ;
    private double _footMarkerX;
    private double _footMarkerY;
    private double _footMarkerZ;
    private double _roiMinX;
    private double _roiMaxX;
    private double _roiMinY;
    private double _roiMaxY;
    private double _roiMinZ;
    private double _roiMaxZ;
    private double _unityAnchorX;
    private double _unityAnchorY;
    private double _unityAnchorZ;
    private double _unityRotationY;
    private double _enterStableSeconds;
    private double _lostGraceSeconds;
    private double _exitConfirmSeconds;
    private double _minSkeletonConfidence;
    private double _footMarkerRadiusMeters;
    private bool _suppressDirty;

    public StationRowViewModel(int stationId, string displayName, string assignedCameraSerial, string deviceType, string depthMode)
    {
        StationId = stationId;
        DisplayName = displayName;
        _assignedCameraSerial = assignedCameraSerial;
        _deviceType = deviceType;
        _depthMode = depthMode;
    }

    public int StationId { get; }

    public string DisplayName { get; }

    public ObservableCollection<JointOverlayPointViewModel> LiveJointPoints { get; } = [];

    public ObservableCollection<BoneOverlaySegmentViewModel> LiveBoneSegments { get; } = [];

    public ObservableCollection<JointOverlayPointViewModel> LivePreviewJointPoints { get; } = [];

    public ObservableCollection<BoneOverlaySegmentViewModel> LivePreviewBoneSegments { get; } = [];

    public string AssignedCameraSerial
    {
        get => _assignedCameraSerial;
        set => SetEditorStringProperty(ref _assignedCameraSerial, value);
    }

    public string DeviceType
    {
        get => _deviceType;
        set => SetEditorStringProperty(ref _deviceType, value);
    }

    public string DepthMode
    {
        get => _depthMode;
        private set => SetProperty(ref _depthMode, value);
    }

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnSkeletonQualityChanged();
            }
        }
    }

    public bool HasPlayer
    {
        get => _hasPlayer;
        set
        {
            if (SetProperty(ref _hasPlayer, value))
            {
                OnPropertyChanged(nameof(PlayerStatusText));
                OnSkeletonQualityChanged();
            }
        }
    }

    public float Confidence
    {
        get => _confidence;
        set
        {
            if (SetProperty(ref _confidence, value))
            {
                OnPropertyChanged(nameof(ConditionSummary));
                OnSkeletonQualityChanged();
            }
        }
    }

    public bool InsideRoi
    {
        get => _insideRoi;
        set
        {
            if (SetProperty(ref _insideRoi, value))
            {
                OnPropertyChanged(nameof(ConditionSummary));
                OnPropertyChanged(nameof(RoiStatusText));
                OnSkeletonQualityChanged();
            }
        }
    }

    public bool InsideFootMarker
    {
        get => _insideFootMarker;
        set
        {
            if (SetProperty(ref _insideFootMarker, value))
            {
                OnPropertyChanged(nameof(ConditionSummary));
                OnPropertyChanged(nameof(MarkerStatusText));
                OnSkeletonQualityChanged();
            }
        }
    }

    public float LostSeconds
    {
        get => _lostSeconds;
        set
        {
            if (SetProperty(ref _lostSeconds, value))
            {
                OnPropertyChanged(nameof(ConditionSummary));
                OnSkeletonQualityChanged();
            }
        }
    }

    public string LastFrameTime
    {
        get => _lastFrameTime;
        set
        {
            if (SetProperty(ref _lastFrameTime, value))
            {
                OnSkeletonQualityChanged();
            }
        }
    }

    public string CameraFrameStatus
    {
        get => _cameraFrameStatus;
        private set => SetProperty(ref _cameraFrameStatus, value);
    }

    public string CameraFrameResolution
    {
        get => _cameraFrameResolution;
        private set => SetProperty(ref _cameraFrameResolution, value);
    }

    public string CameraFrameTime
    {
        get => _cameraFrameTime;
        private set => SetProperty(ref _cameraFrameTime, value);
    }

    public ImageSource? LiveDepthPreview
    {
        get => _liveDepthPreview;
        private set => SetProperty(ref _liveDepthPreview, value);
    }

    public string LivePreviewStatus
    {
        get => _livePreviewStatus;
        private set => SetProperty(ref _livePreviewStatus, value);
    }

    public double LivePreviewCanvasWidth
    {
        get => _livePreviewCanvasWidth;
        private set => SetProperty(ref _livePreviewCanvasWidth, value);
    }

    public double LivePreviewCanvasHeight
    {
        get => _livePreviewCanvasHeight;
        private set => SetProperty(ref _livePreviewCanvasHeight, value);
    }

    public double CameraFps
    {
        get => _cameraFps;
        private set => SetProperty(ref _cameraFps, value);
    }

    public string SkeletonSourceStatus
    {
        get => _skeletonSourceStatus;
        private set
        {
            if (SetProperty(ref _skeletonSourceStatus, value))
            {
                OnPropertyChanged(nameof(SkeletonDiagnosticSummary));
            }
        }
    }

    public int JointCount
    {
        get => _jointCount;
        private set
        {
            if (SetProperty(ref _jointCount, value))
            {
                OnSkeletonQualityChanged();
            }
        }
    }

    public int TrackedJointCount
    {
        get => _trackedJointCount;
        private set
        {
            if (SetProperty(ref _trackedJointCount, value))
            {
                OnSkeletonQualityChanged();
            }
        }
    }

    public double AverageJointConfidence
    {
        get => _averageJointConfidence;
        private set
        {
            if (SetProperty(ref _averageJointConfidence, value))
            {
                OnSkeletonQualityChanged();
            }
        }
    }

    public double JointCoverage
    {
        get => _jointCoverage;
        private set
        {
            if (SetProperty(ref _jointCoverage, value))
            {
                OnSkeletonQualityChanged();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public double PelvisX
    {
        get => _pelvisX;
        set
        {
            if (SetProperty(ref _pelvisX, value))
            {
                OnPropertyChanged(nameof(PelvisCanvasLeft));
                OnPropertyChanged(nameof(SkeletonDiagnosticSummary));
            }
        }
    }

    public double PelvisY
    {
        get => _pelvisY;
        set
        {
            if (SetProperty(ref _pelvisY, value))
            {
                OnPropertyChanged(nameof(SkeletonDiagnosticSummary));
            }
        }
    }

    public double PelvisZ
    {
        get => _pelvisZ;
        set
        {
            if (SetProperty(ref _pelvisZ, value))
            {
                OnPropertyChanged(nameof(PelvisCanvasTop));
                OnPropertyChanged(nameof(SkeletonDiagnosticSummary));
            }
        }
    }

    public double FootX
    {
        get => _footX;
        set => SetProperty(ref _footX, value);
    }

    public double FootZ
    {
        get => _footZ;
        set => SetProperty(ref _footZ, value);
    }

    public double FootMarkerX
    {
        get => _footMarkerX;
        set
        {
            if (SetEditorProperty(ref _footMarkerX, value))
            {
                OnPropertyChanged(nameof(FootMarkerCanvasLeft));
                OnPropertyChanged(nameof(FootMarkerRingCanvasLeft));
            }
        }
    }

    public double FootMarkerY
    {
        get => _footMarkerY;
        set => SetEditorProperty(ref _footMarkerY, value);
    }

    public double FootMarkerZ
    {
        get => _footMarkerZ;
        set
        {
            if (SetEditorProperty(ref _footMarkerZ, value))
            {
                OnPropertyChanged(nameof(FootMarkerCanvasTop));
                OnPropertyChanged(nameof(FootMarkerRingCanvasTop));
            }
        }
    }

    public double RoiMinX
    {
        get => _roiMinX;
        set
        {
            if (SetEditorProperty(ref _roiMinX, value))
            {
                OnRoiCanvasChanged();
            }
        }
    }

    public double RoiMaxX
    {
        get => _roiMaxX;
        set
        {
            if (SetEditorProperty(ref _roiMaxX, value))
            {
                OnRoiCanvasChanged();
            }
        }
    }

    public double RoiMinY
    {
        get => _roiMinY;
        set => SetEditorProperty(ref _roiMinY, value);
    }

    public double RoiMaxY
    {
        get => _roiMaxY;
        set => SetEditorProperty(ref _roiMaxY, value);
    }

    public double RoiMinZ
    {
        get => _roiMinZ;
        set
        {
            if (SetEditorProperty(ref _roiMinZ, value))
            {
                OnRoiCanvasChanged();
            }
        }
    }

    public double RoiMaxZ
    {
        get => _roiMaxZ;
        set
        {
            if (SetEditorProperty(ref _roiMaxZ, value))
            {
                OnRoiCanvasChanged();
            }
        }
    }

    public double UnityAnchorX
    {
        get => _unityAnchorX;
        set => SetEditorProperty(ref _unityAnchorX, value);
    }

    public double UnityAnchorY
    {
        get => _unityAnchorY;
        set => SetEditorProperty(ref _unityAnchorY, value);
    }

    public double UnityAnchorZ
    {
        get => _unityAnchorZ;
        set => SetEditorProperty(ref _unityAnchorZ, value);
    }

    public double UnityRotationY
    {
        get => _unityRotationY;
        set => SetEditorProperty(ref _unityRotationY, value);
    }

    public double EnterStableSeconds
    {
        get => _enterStableSeconds;
        set => SetEditorProperty(ref _enterStableSeconds, value);
    }

    public double LostGraceSeconds
    {
        get => _lostGraceSeconds;
        set => SetEditorProperty(ref _lostGraceSeconds, value);
    }

    public double ExitConfirmSeconds
    {
        get => _exitConfirmSeconds;
        set => SetEditorProperty(ref _exitConfirmSeconds, value);
    }

    public double MinSkeletonConfidence
    {
        get => _minSkeletonConfidence;
        set => SetEditorProperty(ref _minSkeletonConfidence, value);
    }

    public double FootMarkerRadiusMeters
    {
        get => _footMarkerRadiusMeters;
        set => SetEditorProperty(ref _footMarkerRadiusMeters, value);
    }

    public string ConditionSummary =>
        $"confidence {Confidence:0.00}/{MinSkeletonConfidence:0.00} · ROI {InsideRoi} · marker {InsideFootMarker} · lost {LostSeconds:0.00}s/{LostGraceSeconds:0.00}s";

    public string PlayerStatusText => HasPlayer ? "PLAYER LOCKED" : "NO PLAYER";

    public string RoiStatusText => InsideRoi ? "ROI OK" : "ROI OUT";

    public string MarkerStatusText => InsideFootMarker ? "MARKER OK" : "MARKER OUT";

    public string SkeletonQuality
    {
        get
        {
            if (LastFrameTime == "-")
            {
                return "NO FRAME";
            }

            if (!HasPlayer)
            {
                return "NO PLAYER";
            }

            if (JointCount == 0)
            {
                return "NO JOINTS";
            }

            if (AverageJointConfidence < MinSkeletonConfidence)
            {
                return "LOW CONF";
            }

            if (!InsideRoi)
            {
                return "OUTSIDE ROI";
            }

            if (!InsideFootMarker)
            {
                return "MARKER MISS";
            }

            return "TRACKING OK";
        }
    }

    public string SkeletonDiagnosticSummary =>
        $"{SkeletonQuality} · joints {TrackedJointCount}/{JointCount} · avg {AverageJointConfidence:0.00} · pelvis {PelvisX:0.00},{PelvisY:0.00},{PelvisZ:0.00}";

    public double FootMarkerCanvasLeft => WorldXToCanvas(FootMarkerX) - MarkerRadius;

    public double FootMarkerCanvasTop => WorldZToCanvas(FootMarkerZ) - MarkerRadius;

    public double FootMarkerRingCanvasLeft => WorldXToCanvas(FootMarkerX) - 16.0;

    public double FootMarkerRingCanvasTop => WorldZToCanvas(FootMarkerZ) - 16.0;

    public double PelvisCanvasLeft => WorldXToCanvas(PelvisX) - MarkerRadius;

    public double PelvisCanvasTop => WorldZToCanvas(PelvisZ) - MarkerRadius;

    public double RoiCanvasLeft => WorldXToCanvas(Math.Min(RoiMinX, RoiMaxX));

    public double RoiCanvasTop => WorldZToCanvas(Math.Min(RoiMinZ, RoiMaxZ));

    public double RoiCanvasWidth => Math.Max(8.0, WorldXToCanvas(Math.Max(RoiMinX, RoiMaxX)) - RoiCanvasLeft);

    public double RoiCanvasHeight => Math.Max(8.0, WorldZToCanvas(Math.Max(RoiMinZ, RoiMaxZ)) - RoiCanvasTop);

    public void MoveFootMarkerFromCanvas(double canvasX, double canvasY)
    {
        FootMarkerX = CanvasToWorldX(canvasX);
        FootMarkerY = 0.0;
        FootMarkerZ = CanvasToWorldZ(canvasY);
    }

    public void MoveRoiCenterFromCanvas(double canvasX, double canvasY)
    {
        var width = Math.Max(0.1, Math.Abs(RoiMaxX - RoiMinX));
        var depth = Math.Max(0.1, Math.Abs(RoiMaxZ - RoiMinZ));
        var centerX = CanvasToWorldX(canvasX);
        var centerZ = CanvasToWorldZ(canvasY);

        RoiMinX = centerX - width / 2.0;
        RoiMaxX = centerX + width / 2.0;
        RoiMinZ = centerZ - depth / 2.0;
        RoiMaxZ = centerZ + depth / 2.0;
    }

    public void UpdateSkeletonDiagnostics(int jointCount, int trackedJointCount, double averageJointConfidence)
    {
        JointCount = Math.Max(0, jointCount);
        TrackedJointCount = Math.Clamp(trackedJointCount, 0, JointCount);
        AverageJointConfidence = Math.Clamp(averageJointConfidence, 0.0, 1.0);
        JointCoverage = JointCount == 0 ? 0.0 : TrackedJointCount / (double)JointCount;
        OnSkeletonQualityChanged();
    }

    public void UpdateCameraFrame(
        string status,
        string resolution,
        double estimatedFps,
        DateTimeOffset frameTime,
        string skeletonSourceStatus)
    {
        CameraFrameStatus = status;
        CameraFrameResolution = resolution;
        CameraFps = Math.Max(0.0, estimatedFps);
        CameraFrameTime = frameTime.ToLocalTime().ToString("HH:mm:ss.fff");
        SkeletonSourceStatus = skeletonSourceStatus;
    }

    public void UpdateDepthPreview(DepthPreviewFrame? preview, OrbbecPreviewMode previewMode = OrbbecPreviewMode.Depth)
    {
        if (preview is null)
        {
            LivePreviewStatus = $"no {previewMode.ToString().ToLowerInvariant()} preview";
            return;
        }

        var bitmap = BitmapSource.Create(
            preview.Width,
            preview.Height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            preview.Bgra32,
            preview.Stride);
        bitmap.Freeze();
        LiveDepthPreview = bitmap;
        LivePreviewCanvasWidth = preview.Width;
        LivePreviewCanvasHeight = preview.Height;
        LivePreviewStatus = CreateLivePreviewStatus(preview, previewMode);
    }

    private static string CreateLivePreviewStatus(DepthPreviewFrame preview, OrbbecPreviewMode previewMode)
    {
        var mode = previewMode.ToString().ToLowerInvariant();
        if (previewMode != OrbbecPreviewMode.Depth || preview.TotalDepthPixels <= 0)
        {
            return $"{mode} preview {preview.Width}x{preview.Height}";
        }

        if (preview.ValidDepthPixels <= 0)
        {
            return $"depth preview {preview.Width}x{preview.Height} no valid depth";
        }

        var validPercent = preview.ValidDepthPixels * 100.0 / preview.TotalDepthPixels;
        var minMeters = preview.MinDepthMillimeters / 1000.0;
        var maxMeters = preview.MaxDepthMillimeters / 1000.0;
        return $"depth preview {preview.Width}x{preview.Height} valid {validPercent:0}% {minMeters:0.00}-{maxMeters:0.00}m";
    }

    public void UpdateSkeletonOverlay(IEnumerable<JointFrameDto> joints)
    {
        var jointMap = joints
            .Where(joint => !string.IsNullOrWhiteSpace(joint.Name))
            .GroupBy(joint => joint.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        LiveJointPoints.Clear();
        LiveBoneSegments.Clear();
        LivePreviewJointPoints.Clear();
        LivePreviewBoneSegments.Clear();
        var previewProjection = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

        foreach (var joint in jointMap.Values.OrderBy(joint => joint.Name))
        {
            var x = WorldXToCanvas(joint.PositionLocal.X);
            var y = WorldZToCanvas(joint.PositionLocal.Z);
            var kind = JointKind(joint.Name);
            var size = kind == "pelvis" ? 8.0 : kind == "foot" ? 7.0 : 5.0;
            var tooltip = $"{joint.Name} x {joint.PositionLocal.X:0.00} y {joint.PositionLocal.Y:0.00} z {joint.PositionLocal.Z:0.00}m conf {joint.Confidence:P0}";
            LiveJointPoints.Add(new JointOverlayPointViewModel
            {
                Name = joint.Name,
                Kind = kind,
                CanvasLeft = x - size / 2.0,
                CanvasTop = y - size / 2.0,
                Size = size,
                Opacity = Math.Clamp(0.25 + joint.Confidence, 0.25, 1.0),
                Tooltip = tooltip
            });

            var previewPoint = ProjectJointToPreview(joint.PositionLocal);
            if (previewPoint is null)
            {
                continue;
            }

            previewProjection[joint.Name] = previewPoint.Value;
            LivePreviewJointPoints.Add(new JointOverlayPointViewModel
            {
                Name = joint.Name,
                Kind = kind,
                CanvasLeft = previewPoint.Value.X - size / 2.0,
                CanvasTop = previewPoint.Value.Y - size / 2.0,
                Size = size,
                Opacity = Math.Clamp(0.35 + joint.Confidence, 0.35, 1.0),
                Tooltip = tooltip
            });
        }

        foreach (var (from, to) in BonePairs)
        {
            if (!jointMap.TryGetValue(from, out var fromJoint) ||
                !jointMap.TryGetValue(to, out var toJoint))
            {
                continue;
            }

            LiveBoneSegments.Add(new BoneOverlaySegmentViewModel
            {
                X1 = WorldXToCanvas(fromJoint.PositionLocal.X),
                Y1 = WorldZToCanvas(fromJoint.PositionLocal.Z),
                X2 = WorldXToCanvas(toJoint.PositionLocal.X),
                Y2 = WorldZToCanvas(toJoint.PositionLocal.Z),
                Opacity = Math.Clamp((fromJoint.Confidence + toJoint.Confidence) / 2.0, 0.25, 1.0)
            });

            if (previewProjection.TryGetValue(from, out var previewFrom) &&
                previewProjection.TryGetValue(to, out var previewTo))
            {
                LivePreviewBoneSegments.Add(new BoneOverlaySegmentViewModel
                {
                    X1 = previewFrom.X,
                    Y1 = previewFrom.Y,
                    X2 = previewTo.X,
                    Y2 = previewTo.Y,
                    Opacity = Math.Clamp((fromJoint.Confidence + toJoint.Confidence) / 2.0, 0.25, 1.0)
                });
            }
        }
    }

    public void LoadEditorValues(Station station)
    {
        ArgumentNullException.ThrowIfNull(station);

        _suppressDirty = true;
        try
        {
            FootMarkerX = station.FootMarkerCenter.X;
            FootMarkerY = station.FootMarkerCenter.Y;
            FootMarkerZ = station.FootMarkerCenter.Z;
            AssignedCameraSerial = station.AssignedCameraSerial;
            DeviceType = station.DeviceType;
            DepthMode = station.Device.DepthMode;

            RoiMinX = station.TrackingRoi.MinX;
            RoiMaxX = station.TrackingRoi.MaxX;
            RoiMinY = station.TrackingRoi.MinY;
            RoiMaxY = station.TrackingRoi.MaxY;
            RoiMinZ = station.TrackingRoi.MinZ;
            RoiMaxZ = station.TrackingRoi.MaxZ;

            UnityAnchorX = station.UnityAnchor.X;
            UnityAnchorY = station.UnityAnchor.Y;
            UnityAnchorZ = station.UnityAnchor.Z;
            UnityRotationY = station.UnityAnchor.RotationY;

            EnterStableSeconds = station.Thresholds.EnterStableSeconds;
            LostGraceSeconds = station.Thresholds.LostGraceSeconds;
            ExitConfirmSeconds = station.Thresholds.ExitConfirmSeconds;
            MinSkeletonConfidence = station.Thresholds.MinSkeletonConfidence;
            FootMarkerRadiusMeters = station.Thresholds.FootMarkerRadiusMeters;
            IsDirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    public void ApplyEditorValues(Station station)
    {
        ArgumentNullException.ThrowIfNull(station);

        station.FootMarkerCenter = new Vector3Meters(FootMarkerX, FootMarkerY, FootMarkerZ);
        station.AssignedCameraSerial = AssignedCameraSerial.Trim();
        station.DeviceType = string.IsNullOrWhiteSpace(DeviceType) ? "FemtoBolt" : DeviceType.Trim();
        station.TrackingRoi.MinX = Math.Min(RoiMinX, RoiMaxX);
        station.TrackingRoi.MaxX = Math.Max(RoiMinX, RoiMaxX);
        station.TrackingRoi.MinY = Math.Min(RoiMinY, RoiMaxY);
        station.TrackingRoi.MaxY = Math.Max(RoiMinY, RoiMaxY);
        station.TrackingRoi.MinZ = Math.Min(RoiMinZ, RoiMaxZ);
        station.TrackingRoi.MaxZ = Math.Max(RoiMinZ, RoiMaxZ);
        station.UnityAnchor.X = UnityAnchorX;
        station.UnityAnchor.Y = UnityAnchorY;
        station.UnityAnchor.Z = UnityAnchorZ;
        station.UnityAnchor.RotationY = UnityRotationY;
        var lostGraceSeconds = Math.Max(0.0, LostGraceSeconds);
        station.Thresholds.EnterStableSeconds = Math.Max(0.0, EnterStableSeconds);
        station.Thresholds.LostGraceSeconds = lostGraceSeconds;
        station.Thresholds.ExitConfirmSeconds = Math.Max(lostGraceSeconds, ExitConfirmSeconds);
        station.Thresholds.MinSkeletonConfidence = Math.Clamp(MinSkeletonConfidence, 0.0, 1.0);
        station.Thresholds.FootMarkerRadiusMeters = Math.Max(0.0, FootMarkerRadiusMeters);
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    private bool SetEditorProperty(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            if (!_suppressDirty)
            {
                IsDirty = true;
            }

            OnPropertyChanged(nameof(ConditionSummary));
            return true;
        }

        return false;
    }

    private bool SetEditorStringProperty(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (SetProperty(ref field, value, propertyName))
        {
            if (!_suppressDirty)
            {
                IsDirty = true;
            }

            return true;
        }

        return false;
    }

    private void OnRoiCanvasChanged()
    {
        OnPropertyChanged(nameof(RoiCanvasLeft));
        OnPropertyChanged(nameof(RoiCanvasTop));
        OnPropertyChanged(nameof(RoiCanvasWidth));
        OnPropertyChanged(nameof(RoiCanvasHeight));
    }

    private void OnSkeletonQualityChanged()
    {
        OnPropertyChanged(nameof(SkeletonQuality));
        OnPropertyChanged(nameof(SkeletonDiagnosticSummary));
    }

    private static double WorldXToCanvas(double x)
    {
        return Math.Clamp((x - EditorMinX) / (EditorMaxX - EditorMinX), 0.0, 1.0) * EditorCanvasWidth;
    }

    private static double WorldZToCanvas(double z)
    {
        return EditorCanvasTop + Math.Clamp((z - EditorMinZ) / (EditorMaxZ - EditorMinZ), 0.0, 1.0) * EditorCanvasHeight;
    }

    private static double CanvasToWorldX(double canvasX)
    {
        var ratio = Math.Clamp(canvasX / EditorCanvasWidth, 0.0, 1.0);
        return EditorMinX + ratio * (EditorMaxX - EditorMinX);
    }

    private static double CanvasToWorldZ(double canvasY)
    {
        var ratio = Math.Clamp((canvasY - EditorCanvasTop) / EditorCanvasHeight, 0.0, 1.0);
        return EditorMinZ + ratio * (EditorMaxZ - EditorMinZ);
    }

    private (double X, double Y)? ProjectJointToPreview(Vector3Dto position)
    {
        if (position.Z <= 0.05 ||
            LivePreviewCanvasWidth <= 0.0 ||
            LivePreviewCanvasHeight <= 0.0)
        {
            return null;
        }

        var (horizontalFov, verticalFov) = DepthFovDegrees(DepthMode);
        var tanHalfHorizontal = Math.Tan(DegreesToRadians(horizontalFov / 2.0));
        var tanHalfVertical = Math.Tan(DegreesToRadians(verticalFov / 2.0));
        if (tanHalfHorizontal <= 0.0 || tanHalfVertical <= 0.0)
        {
            return null;
        }

        var normalizedX = 0.5 + position.X / (2.0 * position.Z * tanHalfHorizontal);
        var normalizedY = 0.5 + position.Y / (2.0 * position.Z * tanHalfVertical);
        if (normalizedX < 0.0 ||
            normalizedX > 1.0 ||
            normalizedY < 0.0 ||
            normalizedY > 1.0)
        {
            return null;
        }

        return (normalizedX * LivePreviewCanvasWidth, normalizedY * LivePreviewCanvasHeight);
    }

    private static (double Horizontal, double Vertical) DepthFovDegrees(string depthMode)
    {
        var normalized = (depthMode ?? string.Empty)
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized.Contains("WFOV", StringComparison.Ordinal)
            ? (120.0, 120.0)
            : (75.0, 65.0);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static string JointKind(string jointName)
    {
        if (jointName.Equals("Pelvis", StringComparison.OrdinalIgnoreCase))
        {
            return "pelvis";
        }

        return jointName.Contains("Foot", StringComparison.OrdinalIgnoreCase) ||
               jointName.Contains("Ankle", StringComparison.OrdinalIgnoreCase)
            ? "foot"
            : "joint";
    }

    private static readonly (string From, string To)[] BonePairs =
    [
        ("Pelvis", "SpineNavel"),
        ("SpineNavel", "SpineChest"),
        ("SpineChest", "Neck"),
        ("Neck", "Head"),
        ("SpineChest", "ClavicleLeft"),
        ("ClavicleLeft", "ShoulderLeft"),
        ("ShoulderLeft", "ElbowLeft"),
        ("ElbowLeft", "WristLeft"),
        ("SpineChest", "ClavicleRight"),
        ("ClavicleRight", "ShoulderRight"),
        ("ShoulderRight", "ElbowRight"),
        ("ElbowRight", "WristRight"),
        ("Pelvis", "HipLeft"),
        ("HipLeft", "KneeLeft"),
        ("KneeLeft", "AnkleLeft"),
        ("AnkleLeft", "FootLeft"),
        ("Pelvis", "HipRight"),
        ("HipRight", "KneeRight"),
        ("KneeRight", "AnkleRight"),
        ("AnkleRight", "FootRight")
    ];
}
