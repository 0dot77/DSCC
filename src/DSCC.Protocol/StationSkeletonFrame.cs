using MessagePack;

namespace DSCC.Protocol;

[MessagePackObject]
public sealed class StationSkeletonFrame
{
    [Key(0)]
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentProtocolVersion;

    [Key(1)]
    public int StationId { get; set; }

    [Key(2)]
    public string CameraSerial { get; set; } = string.Empty;

    [Key(3)]
    public string DeviceType { get; set; } = string.Empty;

    [Key(4)]
    public long TimestampUsec { get; set; }

    [Key(5)]
    public bool HasPlayer { get; set; }

    [Key(6)]
    public StationStateDto State { get; set; }

    [Key(7)]
    public float Confidence { get; set; }

    [Key(8)]
    public bool IsInsideFootMarker { get; set; }

    [Key(9)]
    public bool IsInsideTrackingRoi { get; set; }

    [Key(10)]
    public float TrackingLostSeconds { get; set; }

    [Key(11)]
    public Vector3Dto PelvisLocal { get; set; } = Vector3Dto.Zero;

    [Key(12)]
    public QuaternionDto BodyRotation { get; set; } = QuaternionDto.Identity;

    [Key(13)]
    public JointFrameDto[] Joints { get; set; } = Array.Empty<JointFrameDto>();

    /// <summary>
    /// Station anchor position in Unity world space, from the station
    /// calibration. Additive field; not affected by skeleton mirroring.
    /// </summary>
    [Key(14)]
    public Vector3Dto AnchorPosition { get; set; } = Vector3Dto.Zero;

    /// <summary>
    /// Station anchor yaw in degrees, from the station calibration.
    /// </summary>
    [Key(15)]
    public float AnchorRotationYDegrees { get; set; }

    /// <summary>
    /// Number of bodies reported by the body tracker for this camera frame.
    /// This is diagnostic metadata; the frame still carries at most one
    /// selected skeleton for the station.
    /// </summary>
    [Key(16)]
    public int BodyCount { get; set; }

    /// <summary>
    /// Native body id selected for this station, or -1 when no skeleton was
    /// selected. K4ABT body ids are tracker-local and useful for detecting
    /// identity swaps during a run.
    /// </summary>
    [Key(17)]
    public long SelectedBodyId { get; set; } = -1;
}
