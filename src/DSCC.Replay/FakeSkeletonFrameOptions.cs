using DSCC.Protocol;

namespace DSCC.Replay;

public sealed class FakeSkeletonFrameOptions
{
    public int StationId { get; set; } = 1;

    public string CameraSerial { get; set; } = "MOCK-REPLAY-001";

    public string DeviceType { get; set; } = "MockReplay";

    public int Fps { get; set; } = 30;

    public long? StartTimestampUsec { get; set; }

    public TimeSpan WarmupOutsideDuration { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan EnterDuration { get; set; } = TimeSpan.FromSeconds(0.5);

    public TimeSpan ActiveDuration { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan LostDuration { get; set; } = TimeSpan.FromSeconds(1.5);

    public TimeSpan ExitedDuration { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan EmptyTailDuration { get; set; } = TimeSpan.FromSeconds(1);

    public TrackingRoi TrackingRoi { get; set; } = TrackingRoi.Default;

    public Vector3Dto FootMarkerCenter { get; set; } = new(0, 0, 2.1f);

    public float FootMarkerRadiusMeters { get; set; } = 0.45f;

    public Vector3Dto OutsideLeftPelvis { get; set; } = new(-1.15f, 1.0f, 2.1f);

    public Vector3Dto InsidePelvis { get; set; } = new(0f, 1.0f, 2.1f);

    public Vector3Dto OutsideRightPelvis { get; set; } = new(1.15f, 1.0f, 2.1f);

    public float ActiveSwayMeters { get; set; } = 0.18f;

    public bool AnimateJoints { get; set; } = true;

    public float LimbMotionMeters { get; set; } = 0.28f;

    public float HeadMotionMeters { get; set; } = 0.08f;

    public float MotionCyclesPerSecond { get; set; } = 0.5f;

    public float InsideConfidence { get; set; } = 0.92f;

    public float OutsideConfidence { get; set; } = 0.25f;

    public TimeSpan Duration =>
        WarmupOutsideDuration +
        EnterDuration +
        ActiveDuration +
        LostDuration +
        ExitedDuration +
        EmptyTailDuration;

    public void Validate()
    {
        if (Fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Fps), "FPS must be greater than zero.");
        }

        if (Duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Fake skeleton sequence duration must be greater than zero.");
        }

        if (FootMarkerRadiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FootMarkerRadiusMeters), "Foot marker radius must be greater than zero.");
        }

        if (LimbMotionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LimbMotionMeters), "Limb motion must be zero or greater.");
        }

        if (HeadMotionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HeadMotionMeters), "Head motion must be zero or greater.");
        }

        if (MotionCyclesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MotionCyclesPerSecond), "Motion cycles per second must be greater than zero.");
        }

        TrackingRoi.Validate();
    }
}
