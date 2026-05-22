using CommunityToolkit.Mvvm.ComponentModel;

namespace DSCC.App.Wpf.ViewModels;

public sealed class DeviceRowViewModel : ObservableObject
{
    private string _status = "disconnected";
    private long _droppedFrames;
    private int _assignedStation;
    private double _fps;
    private long _framesReceived;
    private string _depthResolution = "-";
    private string _colorResolution = "-";
    private string _lastFrameTime = "-";
    private string _skeletonStatus = "not connected";

    public DeviceRowViewModel(
        string deviceType,
        string serial,
        int stationId,
        string connection,
        int fps,
        string depthMode,
        string syncRole)
    {
        DeviceType = deviceType;
        Serial = serial;
        _assignedStation = stationId;
        Connection = connection;
        ConfiguredFps = fps;
        _fps = fps;
        DepthMode = depthMode;
        SyncRole = syncRole;
    }

    public string DeviceType { get; }

    public string Serial { get; }

    public int AssignedStation
    {
        get => _assignedStation;
        set => SetProperty(ref _assignedStation, value);
    }

    public string Connection { get; }

    public int ConfiguredFps { get; }

    public double Fps
    {
        get => _fps;
        set => SetProperty(ref _fps, value);
    }

    public string DepthMode { get; }

    public string SyncRole { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public long DroppedFrames
    {
        get => _droppedFrames;
        set => SetProperty(ref _droppedFrames, value);
    }

    public long FramesReceived
    {
        get => _framesReceived;
        set => SetProperty(ref _framesReceived, value);
    }

    public string DepthResolution
    {
        get => _depthResolution;
        set => SetProperty(ref _depthResolution, value);
    }

    public string ColorResolution
    {
        get => _colorResolution;
        set => SetProperty(ref _colorResolution, value);
    }

    public string LastFrameTime
    {
        get => _lastFrameTime;
        set => SetProperty(ref _lastFrameTime, value);
    }

    public string SkeletonStatus
    {
        get => _skeletonStatus;
        set => SetProperty(ref _skeletonStatus, value);
    }
}
